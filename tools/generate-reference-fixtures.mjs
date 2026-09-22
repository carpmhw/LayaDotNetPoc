import { writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const packageRoot = process.env.LAYA_REFERENCE_NODE_MODULES ?? "/tmp/opencode/laya-reference/node_modules";
const modelRoot = process.env.LAYA_MODEL_ROOT ?? path.join(repositoryRoot, "models", "laya");
const outputPath = process.env.LAYA_FIXTURE_OUTPUT ?? path.join(repositoryRoot, "test-data", "parity-fixtures.json");
const { Laya } = await import(
    pathToFileURL(path.join(packageRoot, "@receptron", "laya", "dist", "laya.js")).href
);
const { buildSequence, renderOptions, serializeState, tempBucket, toInternal, QTYPES } = await import(
    pathToFileURL(path.join(packageRoot, "@receptron", "laya", "dist", "sequence.js")).href
);
const ort = await import(pathToFileURL(path.join(packageRoot, "onnxruntime-node", "dist", "index.js")).href);

/** 產生涵蓋題型、語言、padding 與截斷邊界的 reference inputs。 */
function createCases() {
    return [
        {
            name: "choice-english-two",
            state: "merchant state",
            questions: {
                category: { type: "choice", instructions: "Pick one", criteria: ["food", "travel"] }
            }
        },
        {
            name: "choice-english-three",
            state: "Uber Eats Taiwan order",
            questions: {
                category: { type: "choice", instructions: "Classify this transaction", criteria: ["food", "travel", "retail"] }
            }
        },
        {
            name: "choice-english-five",
            state: "APPLE.COM/BILL subscription",
            questions: {
                category: { type: "choice", instructions: "Choose the best category", criteria: ["food", "travel", "retail", "software", "other"] }
            }
        },
        {
            name: "choice-english-eleven",
            state: "card payment",
            questions: {
                category: {
                    type: "choice",
                    instructions: "Choose one category",
                    criteria: ["food", "travel", "retail", "software", "utilities", "rent", "salary", "transfer", "fees", "cash", "other"]
                }
            }
        },
        {
            name: "noul-english",
            state: "The receipt description is ambiguous",
            questions: {
                needs_review: { type: "noul", instructions: "Is this uncertain?" }
            }
        },
        {
            name: "mixed-batch",
            state: { merchant: "全家", amount: 125.5, currency: "TWD" },
            questions: {
                category: { type: "choice", instructions: "Pick category", criteria: ["food", "retail"] },
                needs_review: { type: "noul", instructions: "Is this uncertain?" }
            }
        },
        {
            name: "chinese-state",
            state: "全家便利商店 台灣大車隊",
            questions: {
                category: { type: "choice", instructions: "選擇分類", criteria: ["餐飲", "交通", "其他"] }
            }
        },
        {
            name: "mixed-language",
            state: { description: "Uber Eats 台灣", note: "APPLE.COM/BILL" },
            questions: {
                category: { type: "choice", instructions: "Classify 中英交易", criteria: ["food", "travel", "other"] }
            }
        },
        {
            name: "mask-token-scrub",
            state: "contains [MASK] literal",
            questions: {
                category: { type: "choice", instructions: "Do not use [MASK] here", criteria: ["[MASK] value", "normal"] }
            }
        },
        {
            name: "long-state-truncation",
            state: "transaction details ".repeat(200),
            questions: {
                category: { type: "choice", instructions: "Classify long state", criteria: ["food", "travel"] }
            }
        }
    ];
}

/** 依 reference 的 softmax 實作產生 fixture probabilities。 */
function softmax(values) {
    const maximum = Math.max(...values);
    const exponentials = values.map((value) => Math.exp(value - maximum));
    const sum = exponentials.reduce((total, value) => total + value, 0);
    return exponentials.map((value) => value / sum);
}

/** 將 question input 轉成 fixture 中穩定且可由 .NET 重建的格式。 */
function fixtureQuestion(name, question) {
    return {
        name,
        type: question.type,
        instructions: question.instructions,
        options: question.type === "choice" || question.type === "score" ? question.criteria : []
    };
}

/** 以 reference collate 規則建立五項 batch tensors。 */
function collate(items, ids) {
    const batchSize = items.length;
    const sequenceLength = Math.max(...items.map((item) => item.ids.length));
    const markerCount = Math.max(...items.map((item) => item.markers.length));
    const inputIds = new Array(batchSize * sequenceLength).fill(ids.pad);
    const attentionMask = new Array(batchSize * sequenceLength).fill(0);
    const markerPositions = new Array(batchSize * markerCount).fill(0);
    const markerMask = new Array(batchSize * markerCount).fill(false);
    const questionTypes = new Array(batchSize);

    items.forEach((item, row) => {
        item.ids.forEach((token, column) => {
            inputIds[row * sequenceLength + column] = token;
            attentionMask[row * sequenceLength + column] = 1;
        });
        item.markers.forEach((marker, column) => {
            markerPositions[row * markerCount + column] = marker;
            markerMask[row * markerCount + column] = true;
        });
        questionTypes[row] = item.qtype;
    });

    return {
        shapes: {
            input_ids: [batchSize, sequenceLength],
            attention_mask: [batchSize, sequenceLength],
            marker_pos: [batchSize, markerCount],
            marker_mask: [batchSize, markerCount],
            qtype: [batchSize]
        },
        input_ids: inputIds,
        attention_mask: attentionMask,
        marker_pos: markerPositions,
        marker_mask: markerMask,
        qtype: questionTypes
    };
}

/** 由同一 bundle 執行 reference sequence、ONNX outputs 與 calibrated probabilities。 */
async function generateFixtures() {
    const laya = await Laya.load({ modelDir: modelRoot });
    const encode = (text) => laya.tok.encode(text, { add_special_tokens: false }).ids;
    const fixtures = [];

    for (const input of createCases()) {
        const questionIds = Object.keys(input.questions);
        const items = questionIds.map((questionId) => {
            const question = toInternal(input.questions[questionId]);
            const sequence = buildSequence(
                encode,
                laya.ids,
                input.state,
                question,
                laya.config.max_len,
                laya.config.head_max_len
            );
            return {
                questionId,
                question,
                ids: sequence.ids,
                markers: sequence.markers,
                qtype: QTYPES[question.t]
            };
        });
        const batch = collate(items, laya.ids);
        const tensors = {
            input_ids: new ort.Tensor("int64", BigInt64Array.from(batch.input_ids, BigInt), batch.shapes.input_ids),
            attention_mask: new ort.Tensor("int64", BigInt64Array.from(batch.attention_mask, BigInt), batch.shapes.attention_mask),
            marker_pos: new ort.Tensor("int64", BigInt64Array.from(batch.marker_pos, BigInt), batch.shapes.marker_pos),
            marker_mask: new ort.Tensor("bool", Uint8Array.from(batch.marker_mask, Boolean), batch.shapes.marker_mask),
            qtype: new ort.Tensor("int64", BigInt64Array.from(batch.qtype, BigInt), batch.shapes.qtype)
        };
        const outputs = await laya.session.run(tensors);
        const logits = Array.from(outputs.logits.data, Number);
        const actProbabilities = Array.from(outputs.act_probs.data, Number);
        const answers = items.map((item, row) => {
            const options = renderOptions(item.question);
            const bucket = tempBucket(item.qtype, options.length);
            const temperature = laya.config.temperature_by_options[bucket] ?? laya.config.temperature[item.qtype];
            const markerCount = batch.shapes.marker_pos[1];
            const probabilities = softmax(
                logits.slice(row * markerCount, row * markerCount + options.length)
                    .map((value) => value / temperature)
            );
            const selectedIndex = probabilities.indexOf(Math.max(...probabilities));
            return {
                questionId: item.questionId,
                options,
                temperature,
                probabilities,
                selectedIndex,
                selectedOption: item.question.t === "noul" ? undefined : options[selectedIndex],
                actProbability: actProbabilities[row * 2]
            };
        });

        fixtures.push({
            name: input.name,
            state: input.state,
            serializedState: serializeState(input.state),
            questions: questionIds.map((questionId) => fixtureQuestion(questionId, input.questions[questionId])),
            sequences: items.map((item) => ({
                questionId: item.questionId,
                tokenIds: item.ids,
                markerPositions: item.markers,
                qtype: item.qtype
            })),
            batch: {
                ...batch,
                shapes: {
                    ...batch.shapes,
                    logits: [items.length, batch.shapes.marker_pos[1]],
                    act_probs: [items.length, 2]
                }
            },
            outputs: {
                logits,
                act_probs: actProbabilities
            },
            answers
        });
    }

    await laya.close();
    await writeFile(
        outputPath,
        JSON.stringify(
            {
                reference: {
                    package: "@receptron/laya",
                    version: "0.1.2",
                    bundleRevision: "68f27dfe5a27a54fb2b1fefc432f43f972e90868"
                },
                fixtures
            },
            null,
            2
        ) + "\n",
        "utf8"
    );
}

await generateFixtures();
