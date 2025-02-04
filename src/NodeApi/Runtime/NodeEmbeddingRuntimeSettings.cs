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

    public static unsafe ConfigureRuntimeCallback CreateConfigureRuntimeCallback(
        NodeEmbeddingRuntimeSettings? settings)
    {
        return new ConfigureRuntimeCallback((platform, config) =>
        {
            if (settings?.NodeApiVersion != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetNodeApiVersion(
                    config, settings.NodeApiVersion.Value)
                    .ThrowIfFailed();
            }
            if (settings?.RuntimeFlags != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetFlags(config, settings.RuntimeFlags.Value)
                    .ThrowIfFailed();
            }
            if (settings?.Args != null || settings?.RuntimeArgs != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetArgs(config, settings.Args, settings.RuntimeArgs)
                    .ThrowIfFailed();
            }
            if (settings?.OnPreload != null)
            {
                JSRuntime.EmbeddingRuntimeConfigOnPreload(
                    config,
                    new node_embedding_runtime_preload_callback(s_runtimePreloadCallback),
                    (nint)GCHandle.Alloc(settings.OnPreload),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            if (settings?.OnLoading != null
                || settings?.MainScript != null)
            {
                LoadingCallback? loadingCallback =
                    settings.MainScript != null
                    ? (NodeEmbeddingRuntime runtime,
                        JSValue process,
                        JSValue require,
                        JSValue runCommonJS)
                        => runCommonJS.Call(JSValue.Null, (JSValue)settings.MainScript)
                    : settings.OnLoading;
                JSRuntime.EmbeddingRuntimeConfigOnLoading(
                    config,
                    new node_embedding_runtime_loading_callback(s_runtimeLoadingCallback),
                    (nint)GCHandle.Alloc(settings.OnLoading),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            if (settings?.OnLoaded != null)
            {
                JSRuntime.EmbeddingRuntimeConfigOnLoaded(
                    config,
                    new node_embedding_runtime_loaded_callback(s_runtimeLoadedCallback),
                    (nint)GCHandle.Alloc(settings.OnLoaded),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            if (settings?.Modules != null)
            {
                foreach (NodeEmbeddingModuleInfo module in settings.Modules)
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
            if (settings?.OnPostTask != null)
            {
                JSRuntime.EmbeddingRuntimeConfigSetTaskRunner(
                    config,
                    new node_embedding_task_post_callback(s_taskPostCallback),
                    (nint)GCHandle.Alloc(settings.OnPostTask),
                    new node_embedding_data_release_callback(s_releaseDataCallback))
                    .ThrowIfFailed();
            }
            settings?.ConfigureRuntime?.Invoke(platform, config);
        });
    }
}
