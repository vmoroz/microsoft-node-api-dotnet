// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using Microsoft.JavaScript.NodeApi.Interop;
using Xunit;
using static Microsoft.JavaScript.NodeApi.Runtime.JSRuntime;

namespace Microsoft.JavaScript.NodeApi.Test;

/// <summary>
/// Unit tests for <see cref="JSValueScope"/>. Validates that scopes can be initialized and nested
/// with intended limitations, and that values can be used only within the scope (and thread)
/// with which they were created.
/// </summary>
public class JSValueScopeTests
{
    private readonly MockJSRuntime _mockRuntime = new();

    private JSValueScope TestRuntimeScope()
    {
        napi_env env = new(Environment.CurrentManagedThreadId);
        var context = new JSRuntimeContext(
            env, _mockRuntime, new MockJSRuntime.SynchronizationContext());
        return JSValueScope.CreateRuntimeScope(env, context);
    }

    [Fact]
    public void CreateRuntimeScope()
    {
        using JSValueScope runtimeScope = TestRuntimeScope();
        Assert.NotNull(runtimeScope.RuntimeContext);
        Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
    }

    [Fact]
    public void CreateNestedRuntimeScope()
    {
        using JSValueScope runtimeScope = TestRuntimeScope();

        using (JSValueScope nestedScope = JSValueScope.CreateRuntimeScope())
        {
            Assert.Same(runtimeScope.RuntimeContext, nestedScope.RuntimeContext);
            Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
        }

        Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
    }

    [Fact]
    public void CreateHandleScopeWithinRuntimeScope()
    {
        using JSValueScope runtimeScope = TestRuntimeScope();

        using (JSValueScope handleScope = JSValueScope.CreateHandleScope())
        {
            Assert.Equal(JSValueScopeType.Handle, JSValueScope.Current.ScopeType);
        }

        Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
    }

    [Fact]
    public void CreateHandleScopeWithinNestedRuntimeScope()
    {
        using JSValueScope runtimeScope = TestRuntimeScope();

        using (JSValueScope nestedScope = JSValueScope.CreateRuntimeScope())
        {
            using (JSValueScope handleScope = JSValueScope.CreateHandleScope())
            {
                Assert.Equal(JSValueScopeType.Handle, JSValueScope.Current.ScopeType);
            }

            Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
        }

        Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
    }

    [Fact]
    public void CreateEscapableScopeWithinRuntimeScope()
    {
        using JSValueScope runtimeScope = TestRuntimeScope();

        using (JSValueScope escapableScope = JSValueScope.CreateEscapableScope())
        {
            Assert.Equal(JSValueScopeType.Escapable, JSValueScope.Current.ScopeType);
        }

        Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
    }

    [Fact]
    public void HandleScopeRequiresParentScope()
    {
        Assert.Throws<InvalidOperationException>(
            () => JSValueScope.CreateHandleScope());
        Assert.Throws<InvalidOperationException>(
            () => JSValueScope.CreateEscapableScope());
    }

    private sealed class DisposableModule : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    [Fact]
    public void DisposableModulesShareContextAndAreDisposedOnceAtTeardown()
    {
        var moduleA = new DisposableModule();
        var moduleB = new DisposableModule();

        napi_env env = new(Environment.CurrentManagedThreadId);
        var context = new JSRuntimeContext(
            env, _mockRuntime, new MockJSRuntime.SynchronizationContext());

        using (JSValueScope.CreateRuntimeScope(env, context))
        {
            // Two generated modules loaded into one managed host share this context; each opens a
            // module-boundary scope and exports its instance.
            using (JSValueScope.CreateModuleScope(env))
            {
                new JSModuleBuilder<DisposableModule>().ExportModule(
                    moduleA, (JSObject)JSValue.CreateObject());
            }

            using (JSValueScope.CreateModuleScope(env))
            {
                new JSModuleBuilder<DisposableModule>().ExportModule(
                    moduleB, (JSObject)JSValue.CreateObject());
            }

            // Loading the second module must not dispose the first.
            Assert.Equal(0, moduleA.DisposeCount);
            Assert.Equal(0, moduleB.DisposeCount);
        }

        context.Dispose();

        // Each module instance is disposed exactly once at env teardown.
        Assert.Equal(1, moduleA.DisposeCount);
        Assert.Equal(1, moduleB.DisposeCount);
    }

    private sealed class EqualDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        // All instances compare equal, to prove module disposables dedupe by identity, not Equals.
        public override bool Equals(object? obj) => obj is EqualDisposable;

        public override int GetHashCode() => 0;
    }

    [Fact]
    public void ModuleDisposablesAreDedupedByIdentityNotEquality()
    {
        var moduleA = new EqualDisposable();
        var moduleB = new EqualDisposable();
        Assert.True(moduleA.Equals(moduleB));

        napi_env env = new(Environment.CurrentManagedThreadId);
        var context = new JSRuntimeContext(
            env, _mockRuntime, new MockJSRuntime.SynchronizationContext());

        context.AddModuleDisposable(moduleA);
        context.AddModuleDisposable(moduleB);
        context.AddModuleDisposable(moduleA); // Re-adding the same instance is a no-op.

        context.Dispose();

        // Both distinct instances are disposed once, despite comparing equal.
        Assert.Equal(1, moduleA.DisposeCount);
        Assert.Equal(1, moduleB.DisposeCount);
    }

    [Fact]
    public void AccessValueFromClosedScope()
    {
        using JSValueScope rootScope = TestRuntimeScope();

        JSValueScope handleScope;
        JSValue objectValue;
        using (handleScope = JSValueScope.CreateHandleScope())
        {
            objectValue = JSValue.CreateObject();
            Assert.True(objectValue.IsObject());
        }

        Assert.True(handleScope.IsDisposed);
        JSValueScopeClosedException ex = Assert.Throws<JSValueScopeClosedException>(
            () => objectValue.IsObject());
        Assert.Equal(handleScope, ex.Scope);
    }

    [Fact]
    public void AccessPropertyKeyFromClosedScope()
    {
        using JSValueScope rootScope = TestRuntimeScope();

        JSValue objectValue = JSValue.CreateObject();
        JSValue propertyKey;

        JSValueScope handleScope;
        using (handleScope = JSValueScope.CreateHandleScope())
        {
            propertyKey = "test";
            Assert.True(propertyKey.IsString());
        }

        // The property key scope was closed so it's not valid to use as a method argument.
        Assert.True(handleScope.IsDisposed);
        JSValueScopeClosedException ex = Assert.Throws<JSValueScopeClosedException>(
            () => objectValue[propertyKey]);
        Assert.Equal(handleScope, ex.Scope);

        // The object value scope was not closed so it's still valid.
        Assert.True(objectValue.IsObject());
    }

    [Fact]
    public void CreateValueFromDifferentThread()
    {
        using JSValueScope rootScope = TestRuntimeScope();

        // Run in a new thread which will not have any current scope.
        TestUtils.RunInThread(() =>
        {
            Assert.Throws<JSInvalidThreadAccessException>(() => JSValueScope.Current);
            JSInvalidThreadAccessException ex = Assert.Throws<JSInvalidThreadAccessException>(
                () => new JSObject());
            Assert.Null(ex.CurrentScope);
            Assert.Null(ex.TargetScope);
        }).Wait();
    }

    [Fact]
    public void AccessValueFromDifferentThread()
    {
        using JSValueScope rootScope = TestRuntimeScope();
        JSValue objectValue = JSValue.CreateObject();

        // Run in a new thread which will not have any current scope.
        TestUtils.RunInThread(() =>
        {
            Assert.Throws<JSInvalidThreadAccessException>(() => JSValueScope.Current);
            JSInvalidThreadAccessException ex = Assert.Throws<JSInvalidThreadAccessException>(
                () => objectValue.IsObject());
            Assert.Null(ex.CurrentScope);
            Assert.Equal(rootScope, ex.TargetScope);
        }).Wait();
    }

    [Fact]
    public void AccessValueFromDifferentRootScope()
    {
        using JSValueScope rootScope1 = TestRuntimeScope();
        JSValue objectValue = JSValue.CreateObject();

        // Run in a new thread and establish another root scope there.
        TestUtils.RunInThread(() =>
        {
            using JSValueScope rootScope2 = TestRuntimeScope();
            Assert.Equal(JSValueScopeType.RuntimeContext, JSValueScope.Current.ScopeType);
            JSInvalidThreadAccessException ex = Assert.Throws<JSInvalidThreadAccessException>(
                () => objectValue.IsObject());
            Assert.Equal(rootScope2, ex.CurrentScope);
            Assert.Equal(rootScope1, ex.TargetScope);
        }).Wait();
    }

    // The module instance is captured through a shared holder: descriptors take the holder during
    // initialization (before the instance exists) and observe the instance once dispatch assigns it.
    // Nested handle/escapable scopes inherit the same holder, so Current.Module round-trips through it.
    [Fact]
    public void ModuleInstanceRoundTripsThroughSharedHolder()
    {
        using JSValueScope runtimeScope = TestRuntimeScope();

        // The runtime scope mints a holder; the module instance is not assigned yet.
        StrongBox<object?> holder = JSValueScope.Current.ModuleHolder!;
        Assert.NotNull(holder);
        Assert.Null(JSValueScope.Current.Module);

        object moduleInstance = new();
        using (JSValueScope handleScope = JSValueScope.CreateHandleScope())
        {
            // Inner scopes inherit the same holder instance.
            Assert.Same(holder, JSValueScope.Current.ModuleHolder);

            // Assigning through the shared holder (as dispatch does) is visible as Current.Module.
            holder.Value = moduleInstance;
            Assert.Same(moduleInstance, JSValueScope.Current.Module);
        }

        // The instance remains visible in the parent scope after the nested scope closes.
        Assert.Same(moduleInstance, JSValueScope.Current.Module);
    }

    // An escapable scope promotes one value to its parent so the value stays usable after the inner
    // scope closes, while a value that was not escaped becomes invalid once the scope is disposed.
    [Fact]
    public void EscapableScopeEscapesValue()
    {
        using JSValueScope rootScope = TestRuntimeScope();

        JSValue escaped;
        JSValue notEscaped;
        JSValueScope escapableScope;
        using (escapableScope = JSValueScope.CreateEscapableScope())
        {
            notEscaped = JSValue.CreateObject();
            escaped = escapableScope.Escape(JSValue.CreateObject());

            Assert.True(escaped.IsObject());
            Assert.True(notEscaped.IsObject());
        }

        // The escaped value was promoted to the parent scope, so it remains usable.
        Assert.True(escapableScope.IsDisposed);
        Assert.True(escaped.IsObject());

        // The value that was not escaped belonged to the now-closed scope.
        JSValueScopeClosedException ex = Assert.Throws<JSValueScopeClosedException>(
            () => notEscaped.IsObject());
        Assert.Equal(escapableScope, ex.Scope);
    }

    // With no explicit context and no parent scope, CreateRuntimeScope recovers the context from the
    // env instance data (JSRuntimeContext.FromEnv) -- the path the dynamic module entry point relies
    // on to resolve the context when no scope is on the thread yet.
    [Fact]
    public void CreateRuntimeScopeResolvesContextFromEnv()
    {
        napi_env env = new(Environment.CurrentManagedThreadId);
        var context = new JSRuntimeContext(
            env, _mockRuntime, new MockJSRuntime.SynchronizationContext());

        // The context registered itself in the env instance data, so FromEnv resolves it.
        Assert.Same(context, JSRuntimeContext.FromEnv(env));

        using JSValueScope runtimeScope = JSValueScope.CreateRuntimeScope(env);
        Assert.Same(context, runtimeScope.RuntimeContext);
        Assert.Same(context, JSValueScope.Current.RuntimeContext);
    }

    // JSRuntimeContext.Create is the public factory used by AOT entry points and embedders. It uses
    // the provided runtime, and a runtime scope over the context resolves it as the current context.
    [Fact]
    public void CreateRuntimeContextFactoryUsesProvidedRuntime()
    {
        napi_env env = new(Environment.CurrentManagedThreadId);
        JSRuntimeContext context = JSRuntimeContext.Create(
            env, _mockRuntime, new MockJSRuntime.SynchronizationContext());

        Assert.Same(_mockRuntime, context.Runtime);
        Assert.False(context.IsDisposed);

        using JSValueScope runtimeScope = JSValueScope.CreateRuntimeScope(env, context);
        Assert.Same(context, JSValueScope.Current.RuntimeContext);
        Assert.Same(context, JSRuntimeContext.Current);
    }

    // A runtime-context scope installs the context's synchronization context as the thread's current
    // one for its lifetime (so await continuations marshal back to the JS thread) and restores the
    // previously-current one when disposed.
    [Fact]
    public void RuntimeScopeInstallsAndRestoresSynchronizationContext()
    {
        System.Threading.SynchronizationContext? previous =
            System.Threading.SynchronizationContext.Current;

        napi_env env = new(Environment.CurrentManagedThreadId);
        var syncContext = new MockJSRuntime.SynchronizationContext();
        var context = new JSRuntimeContext(env, _mockRuntime, syncContext);

        using (JSValueScope runtimeScope = JSValueScope.CreateRuntimeScope(env, context))
        {
            Assert.Same(syncContext, System.Threading.SynchronizationContext.Current);
        }

        Assert.Same(previous, System.Threading.SynchronizationContext.Current);
    }
}
