// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
}
