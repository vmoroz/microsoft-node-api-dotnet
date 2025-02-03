// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using static NodejsEmbedding;
using static NodejsRuntime;

public class NodeEmbeddingPlatformSettings
{
    public NodeEmbeddingPlatformFlags? PlatformFlags { get; set; }
    public string[]? Args { get; set; }
    public ConfigurePlatformCallback? ConfigurePlatform { get; set; }
}
