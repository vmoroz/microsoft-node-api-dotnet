// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Microsoft.JavaScript.NodeApi.Runtime.NodeEmbedding;
using static Microsoft.JavaScript.NodeApi.Runtime.NodejsRuntime;

public sealed class NodeEmbeddingRuntimeSettings
{
    public int? NodeApiVersion { get; set; }
    public NodeEmbeddingRuntimeFlags? RuntimeFlags { get; set; }
    public string[]? Args { get; set; }
    public string[]? RuntimeArgs { get; set; }
    public PreloadCallback? OnPreload { get; set; }
    public string? MainScript { get; set; }
    public LoadingCallback? OnLoading { get; set; }
    public LoadedCallback? OnLoaded { get; set; }
    public IEnumerable<NodeEmbeddingModuleInfo>? Modules { get; set; }
    public PostTaskCallback? OnPostTask { get; set; }
    public ConfigureRuntimeCallback? ConfigureRuntime { get; set; }

    public static JSRuntime JSRuntime => NodeEmbedding.JSRuntime;

    public unsafe ConfigureRuntimeCallback CreateConfigureRuntimeCallback()
    {
        return new ConfigureRuntimeCallback((platform, config) =>
        {
            if (NodeApiVersion != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetNodeApiVersion(
                    config, NodeApiVersion.Value)
                    .ThrowIfFailed();
            }
            if (RuntimeFlags != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetFlags(config, RuntimeFlags.Value)
                    .ThrowIfFailed();
            }
            if (Args != null || RuntimeArgs != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetArgs(config, Args, RuntimeArgs)
                    .ThrowIfFailed();
            }
            if (OnPreload != null)
            {
                JSRuntime.EmbeddingRuntimeConfigOnPreload(
                    config,
                    new node_embedding_runtime_preload_callback(s_runtimePreloadCallback),
                    (nint)GCHandle.Alloc(OnPreload),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            if (OnLoading != null || MainScript != null)
            {
                LoadingCallback? loadingCallback =
                    MainScript != null
                    ? (NodeEmbeddingRuntime runtime,
                        JSValue process,
                        JSValue require,
                        JSValue runCommonJS)
                        => runCommonJS.Call(JSValue.Null, (JSValue)MainScript)
                    : OnLoading;
                JSRuntime.EmbeddingRuntimeConfigOnLoading(
                    config,
                    new node_embedding_runtime_loading_callback(s_runtimeLoadingCallback),
                    (nint)GCHandle.Alloc(OnLoading),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            if (OnLoaded != null)
            {
                JSRuntime.EmbeddingRuntimeConfigOnLoaded(
                    config,
                    new node_embedding_runtime_loaded_callback(s_runtimeLoadedCallback),
                    (nint)GCHandle.Alloc(OnLoaded),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            if (Modules != null)
            {
                foreach (NodeEmbeddingModuleInfo module in Modules)
                {
                    JSRuntime.EmbeddingRuntimeConfigAddModule(
                        config,
                        (module.Name ?? throw new ArgumentException("Module name is missing"))
                            .AsSpan(),
                        new node_embedding_module_initialize_callback(s_moduleInitializeCallback),
                        (nint)GCHandle.Alloc(module.OnInitialize
                            ?? throw new ArgumentException("Module initialization is missing")),
                        new node_embedding_data_release_callback(s_releaseDataCallback),
                        module.NodeApiVersion ?? 0)
                        .ThrowIfFailed();
                }
            }
            if (OnPostTask != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetTaskRunner(
                    config,
                    new node_embedding_task_post_callback(s_taskPostCallback),
                    (nint)GCHandle.Alloc(OnPostTask),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            ConfigureRuntime?.Invoke(platform, config);
        });
    }
}
