{
  "schemaVersion": 1,
  "auditStatus": "complete",
  "campaignId": "phase3a-formal-20260925-v2",
  "sourceIdentity": {
    "commit": "98f4840c2b7746b94825fe904f9228ed63c43392",
    "dirty": true,
    "dirtyIdentity": "d77ea11b5bd8804606db84209fe498b0cee25ac9a1eb503ae4cd1622a509b778"
  },
  "policySha256": "fca022bdbcc450d95eec4c28c4bcd00cc7aba9fec68fb5249c6f341f35628a68",
  "owners": [
    {
      "owner": "Input OrtValues and backing arrays",
      "source": "src/Laya.Core/Inference/LayaInputTensorOwner.cs",
      "successPath": "LayaInferenceRunner.Run scopes one owner with using; Dispose atomically clears input dictionary and releases five OrtValues in reverse order while keeping backing arrays alive through disposal.",
      "partialConstructionFailure": "Create catches factory failures and disposes all previously created values; disposal exceptions are aggregated with the construction error.",
      "probeRunOnlyPath": "Each RunOnlyWorker owns one LayaInputTensorOwner; workers are drained before scenario disposal.",
      "tests": [
        "LayaInputTensorOwnerTests.Create_OwnsFiveInputsAndDisposesThemOnce",
        "LayaInputTensorOwnerTests.Create_DisposesPreviouslyCreatedValuesWhenFactoryThrows",
        "LayaInferenceRunnerLifecycleTests.Run_DisposesInputValuesWhenNativeRunThrows"
      ]
    },
    {
      "owner": "RunOptions and native output OrtValues",
      "source": "src/Laya.Core/Inference/LayaInferenceRunner.cs",
      "successPath": "RunOptions and IDisposableReadOnlyCollection<OrtValue> outputs are scoped with using; output tensor values are copied into managed arrays before the output collection is released.",
      "failurePath": "The same using scopes release input owner, RunOptions and returned output collection when shape validation, output copy, or native Run fails.",
      "runOnlyPath": "RunOnlyWorker retains per-worker RunOptions and fixed input owner; each Run output collection is disposed per operation.",
      "tests": [
        "LayaInferenceRunnerLifecycleTests.Run_DisposesInputValuesWhenNativeRunThrows",
        "LayaInputTensorOwnerTests.Create_DisposesPreviouslyCreatedValuesWhenFactoryThrows"
      ]
    },
    {
      "owner": "ONNX InferenceSession and SessionOptions",
      "source": "src/Laya.Core/Inference/LayaOnnxSession.cs",
      "successPath": "Open transfers a successfully initialized InferenceSession to LayaOnnxSession; SessionOptions are disposed in finally. LayaOnnxSession.Dispose atomically clears and disposes the native session.",
      "initializationFailure": "Open disposes any session not transferred to the wrapper and always disposes SessionOptions; CPU provider configuration failure disposes SessionOptions before rethrow.",
      "tests": [
        "LayaOnnxSessionOptionsTests.CreateSessionOptions_EnablesCpuArenaWhenRequested",
        "LayaOnnxSessionOptionsTests.CreateSessionOptions_DisablesCpuArenaWhenRequested",
        "LayaInferenceRunnerLifecycleTests.Run_DisposesInputValuesWhenNativeRunThrows"
      ]
    },
    {
      "owner": "Long-lived decision engine and tokenizer",
      "source": "src/Laya.Core/Inference/LayaDecisionEngine.cs; src/Laya.Core/Tokenization/LayaTokenizer.cs",
      "successPath": "LayaDecisionEngine owns the tokenizer and session; Dispose releases tokenizer then session and is idempotent. LayaTokenizer.Load disposes a partially created tokenizer in finally unless ownership transfers to the wrapper.",
      "initializationFailure": "LayaDecisionEngine.Open disposes a loaded tokenizer and session if engine construction fails.",
      "tests": [
        "LayaDecisionEngineTests.Dispose_ReleasesInjectedSessionAndTokenizerAfterRepeatedDecisions",
        "LayaTokenizerTests.Load_RejectsMissingSpecialTokenConfig"
      ]
    },
    {
      "owner": "Probe scenario execution and run-only worker resources",
      "source": "tools/Laya.MemoryProbe/Scenarios/MemoryProbeScenarioExecution.cs",
      "successPath": "Scenario initialization failure calls Dispose; normal shutdown drains workers, then disposes decision engine/session/tokenizer owners. Run-only worker creation releases input owner if RunOptions creation fails; worker Dispose releases both resources and aggregates failures.",
      "tests": [
        "MemoryProbeScenarioExecutionTests.Create_ComponentScenariosDoNotOpenInferenceSession",
        "BoundedProbeCoordinatorTests.ExecuteAsync_CancellationDrainsInFlightWorkers",
        "BoundedProbeCoordinatorTests.ExecuteAsync_UsesBoundedWorkersAndDispatchesEachRequestOnce"
      ]
    }
  ],
  "measuredTotalAllocatedBytes": [
    {
      "runId": "phase3a-formal-20260925-v2-baseline-full-pipeline",
      "request0": 2217624,
      "request5000": 130840688,
      "postDispose": 131411616,
      "measuredRequestIntervalDeltaBytes": 128623064,
      "errors": 0,
      "integrityFailures": 0
    },
    {
      "runId": "phase3a-formal-20260925-v2-component-full-pipeline",
      "request0": 2205216,
      "request5000": 130834944,
      "postDispose": 131405888,
      "measuredRequestIntervalDeltaBytes": 128629728,
      "errors": 0,
      "integrityFailures": 0
    },
    {
      "runId": "phase3a-formal-20260925-v2-component-run-only",
      "request0": 2117448,
      "request5000": 14587256,
      "postDispose": 14762256,
      "measuredRequestIntervalDeltaBytes": 12469808,
      "errors": 0,
      "integrityFailures": 0
    },
    {
      "runId": "phase3a-formal-20260925-v2-session-recreate-10",
      "request0": 1717384,
      "cycle10PostDispose": 4967400,
      "cycles": 10,
      "errors": 0
    },
    {
      "runId": "phase3a-formal-20260925-v2-session-recreate-100",
      "request0": 1725176,
      "cycle100PostDispose": 31014320,
      "cycles": 100,
      "errors": 0
    }
  ],
  "lifecycleDisposition": {
    "leakReproduced": false,
    "retentionSourceIdentified": false,
    "lifecycleAuditConfirmsUndisposedResource": false,
    "nativeRetentionObserved": true,
    "boundedNativeRetention": false,
    "nativeRetentionExplained": false,
    "allocatorComparisonSupportsPlateau": false,
    "sessionRecreateUnboundedGrowth": false,
    "arenaTradeoffExplained": false,
    "deploymentLimitDocumented": true,
    "monitoringConditionsDocumented": false,
    "finding": "Source review and covered lifecycle tests found no confirmed undisposed owner. Native/process RSS remains above T0 after session disposal and the 100-cycle run shows non-zero retained process memory; the allocation owner is not attributed, so this is a retention observation, not Leak Confirmed or a proven bounded plateau. The formal managed-heap late segment is incomplete at three unavailable counter samples."
  }
}
