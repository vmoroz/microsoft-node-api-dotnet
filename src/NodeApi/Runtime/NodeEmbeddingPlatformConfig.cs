// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using static Microsoft.JavaScript.NodeApi.Runtime.NodejsRuntime;

namespace Microsoft.JavaScript.NodeApi.Runtime;

public struct NodeEmbeddingPlatformConfig
{
    private readonly node_embedding_platform_config _handle = default;
    public static JSRuntime JSRuntime => NodejsEmbedding.JSRuntime;

    public NodeEmbeddingPlatformConfig()
    {
    }

    public NodeEmbeddingPlatformConfig(node_embedding_platform_config handle)
    {
        _handle = handle;
    }

    public node_embedding_platform_config Handle => _handle;

    public static implicit operator NodeEmbeddingPlatformConfig(
        node_embedding_platform_config handle) => new(handle);
    public static explicit operator node_embedding_platform_config(
        NodeEmbeddingPlatformConfig value) => value.Handle;

    public void SetFlags(NodeEmbeddingPlatformFlags flags)
    {
        JSRuntime.EmbeddingPlatformConfigSetFlags(_handle, flags).ThrowIfFailed();
    }
}
