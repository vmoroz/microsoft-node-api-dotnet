// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.JavaScript.NodeApi.Runtime;
using static Microsoft.JavaScript.NodeApi.Runtime.JSRuntime;

namespace Microsoft.JavaScript.NodeApi.DotNetHost;

/// <summary>
/// A minimal strong reference to a JS value for use by the native host.
/// </summary>
/// <remarks>
/// The native host runs in a <see cref="JSValueScopeType.NoContext"/> scope, which has no
/// <see cref="Interop.JSRuntimeContext"/> and therefore cannot use <see cref="JSReference"/>
/// (a <see cref="JSReference"/> requires a context to know when its <c>napi_env</c> is gone).
/// <para/>
/// Unlike <see cref="JSReference"/>, this type has no finalizer. The referenced value is held for
/// the lifetime of the <c>napi_env</c> (the host's re-init cache); a Node-API reference is owned
/// by its <c>napi_env</c> and is released by Node when the environment is torn down, so releasing
/// it from the .NET GC finalizer thread -- which has no live JS environment -- would be a
/// use-after-free. It is only ever used on the JS thread while the environment is alive.
/// </remarks>
internal sealed class JSHostReference
{
    private readonly napi_ref _handle;
    private readonly napi_env _env;
    private readonly JSRuntime _runtime;

    public JSHostReference(JSValue value)
    {
        JSValueScope scope = JSValueScope.Current;
        _env = scope.UncheckedEnvironmentHandle;
        _runtime = scope.Runtime;
        _runtime.CreateReference(_env, (napi_value)value, 1u, out _handle).ThrowIfFailed();
    }

    /// <summary>
    /// Gets the referenced JS value. Must be called on the JS thread while the environment
    /// is alive.
    /// </summary>
    public JSValue GetValue()
    {
        _runtime.GetReferenceValue(_env, _handle, out napi_value result).ThrowIfFailed();
        return new JSValue(result);
    }
}
