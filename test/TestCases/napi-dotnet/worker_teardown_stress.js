// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Stress variant of worker_teardown.js. It repeatedly creates a Worker that loads the .NET host
// (only inside the worker), waits for it to initialize, then terminates it. Each load initializes
// a native host and a managed host for that worker's environment; terminating the worker tears the
// environment down, which runs the native host's instance-data finalizer. That finalizer notifies
// the managed host to dispose its context, all without calling into JavaScript (the environment is
// being destroyed). Looping exercises that per-environment init/teardown path many times to
// surface teardown-ordering crashes or leaked references (the crash class this guards against).
//
// The workers run one at a time (each is terminated before the next is created), so this never
// holds two host instances at once. As with worker_teardown.js this validates the hosted host
// module and runs under HostedClrTests only (the name contains "worker_teardown", which also
// excludes it from NativeAotTests).

const assert = require('assert');
const { Worker, isMainThread, parentPort } = require('worker_threads');

const iterations = 8;

if (isMainThread) {
  (async () => {
    for (let i = 0; i < iterations; i++) {
      const worker = new Worker(__filename);
      await new Promise((resolve, reject) => {
        worker.once('message', (message) => {
          try {
            assert.strictEqual(message, 'ready');
            resolve();
          } catch (err) {
            reject(err);
          }
        });
        worker.once('error', reject);
      });
      await worker.terminate();
    }

    // Keep the process alive briefly so any teardown crash surfaces as a non-zero exit code
    // instead of being skipped by an immediate process exit.
    setTimeout(() => process.exit(0), 300);
  })().catch((err) => { throw err; });
} else {
  // Load the native host ONLY in the worker.
  const binding = require('../common').binding;
  assert.ok(binding);
  parentPort.postMessage('ready');
}
