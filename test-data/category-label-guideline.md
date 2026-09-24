# Phase 2 Category Label Guideline

版本：`phase2-v1`  
資料來源：synthetic，所有描述為去識別化模板，不含帳號、卡號、姓名或完整地址。  
標註方式：依交易描述、金額、幣別與 debit／credit direction 由人工覆核規則標註；商戶名稱本身不是唯一證據。

## 固定 11 類

| Category | 定義 | 正例 | 不應直接歸類的情況 |
|---|---|---|---|
| `food` | 餐廳、外送、咖啡、食品雜貨與餐飲採購。 | Uber Eats、咖啡店、超市食品。 | 便利商店若只寫店名且用途不明，先依模糊規則判定。 |
| `transport` | 交通運輸、計程車、公共運輸、停車、高速公路與車資。 | 台灣大車隊、高鐵、停車費。 | 車輛維修或保險不是交通。 |
| `shopping` | 一般商品、服飾、家用品與非食品零售。 | 百貨、電商商品、服飾店。 | 明確娛樂、醫療或水電服務應使用專門類別。 |
| `utilities` | 水、電、瓦斯、電信、網路與固定公共服務帳單。 | 電費、手機月租、寬頻。 | 信用卡繳款不是 utilities。 |
| `transfer` | 一般個人或帳戶間轉帳，無薪資、投資或特定帳單用途。 | 轉帳給房租收款人、帳戶間轉入。 | 薪資、券商入金、信用卡繳款有明確專類時不得泛化成 transfer。 |
| `salary` | 雇主薪資、獎金或明確 payroll 入帳。 | 薪資、月薪、bonus payroll。 | 不明入帳不得因為 credit 就標 salary。 |
| `bank_fee` | 銀行、ATM、匯款或帳戶維護手續費。 | 匯費、跨行費、帳戶管理費。 | 商品或服務價格中的 service fee 若無銀行語境，依主要交易用途。 |
| `investment` | 股票、基金、券商、加密資產或投資帳戶交易。 | 券商入金、基金申購。 | 一般銀行轉帳若沒有投資語境仍為 transfer。 |
| `medical` | 醫院、診所、藥局、牙科與醫療檢查。 | 醫院掛號、藥局、牙醫。 | 保健食品或美容服務沒有醫療證據時不直接標 medical。 |
| `entertainment` | 串流、電影、遊戲、音樂、書籍與休閒活動。 | Netflix、電影院、遊戲平台。 | 教育課程或一般商品需依描述判斷，不因 online 就標 entertainment。 |
| `other` | 無法依規則可靠歸入上述類別，或資訊不足且沒有安全的專類證據。 | 不明商戶、雜項調整。 | 不得把可由明確關鍵詞判定的交易塞進 other。 |

## 模糊商戶與便利商店

- 便利商店店名若含食品、飲料、咖啡、便當等用途，標 `food`。
- 便利商店若明確購買交通卡加值、代收水電或帳單，依明確用途標 `transport` 或 `utilities`。
- 便利商店只有店名、沒有品項或用途時標 `other`，不可只因商戶常見而猜測 food。
- 商戶名稱同時可能代表商品與服務時，優先使用描述中的用途；若仍無法區分，標 `other` 並在 `notes` 記錄 `ambiguous-merchant`。
- 同一商戶模板最多五筆；不同金額不會自動構成不同 group，近似描述仍須放在同一 group 以避免 split leakage。

## 信用卡繳款與 transfer

- 「信用卡繳款／卡費／card payment」本資料版本一律標 `transfer`，因為它是對信用卡帳戶的資金移轉而非水電服務；不可因為出現 bill 就標 `utilities`，`notes` 必須記錄 `credit-card-payment`。
- 一般「轉帳／匯款」沒有薪資、投資、卡費或銀行費關鍵詞時標 `transfer`。
- credit direction 不代表 salary；debit direction 也不排除 transfer 或 investment。
- 「銀行手續費／匯費」優先標 `bank_fee`，即使同列描述含 transfer。

## Other 與覆核

- `other` 是資訊不足或真正不屬於固定 10 類的保守標籤，不是模型無法判斷時的便利答案。
- 標註者至少記錄 description、amount、direction、適用規則與任何模糊原因。
- 發現疑似錯標時只建立覆核紀錄，不直接修改既有 CSV。確認修正後必須升 dataset version、重算 dataset hash／manifest，並重跑 English 與 multilingual。
- 覆核需由第二位標註者檢查；若兩人不同意，保留 `other` 或在 notes 記錄爭議，不以模型預測反推 label。
- 去識別檢查須移除帳號、卡號、姓名、電話、email、完整地址、精確交易識別資訊；報告只可引用 row id 與去識別 description。
