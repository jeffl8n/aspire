// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Aspire.Hosting.Tests.ApplicationModel;
public class ReferenceExpressionTests
{
    [Theory]
    [InlineData("{x}", "{x}")]
    [InlineData("{{x}}", "{x}")]
    [InlineData("{x", "{x")]
    [InlineData("x}", "x}")]
    [InlineData("{1 var}", "{1 var}")]
    [InlineData("{var 1}", "{var 1}")]
    [InlineData("{1myVar}", "{1myVar}")]
    [InlineData("{myVar1}", "{myVar1}")]
    public void ReferenceExpressionHandlesValueWithNonParameterBrackets(string input, string expected)
    {
        var expr = ReferenceExpression.Create($"{input}").ValueExpression;
        Assert.Equal(expected, expr);
    }

    [Theory]
    [InlineData("{0}", "abc123", "abc123")]
    [InlineData("{0} test", "abc123", "abc123 test")]
    [InlineData("test {0}", "abc123", "test abc123")]
    public void ReferenceExpressionHandlesValueWithParameterBrackets(string input, string parameterValue, string expected)
    {
        var expr = ReferenceExpression.Create($"{input}", [new HostUrl("test")], [parameterValue]).ValueExpression;
        Assert.Equal(expected, expr);
    }

    public static readonly object[][] ValidFormattingInParameterBracketCases = [
        ["{0:D}", new DateTime(2024,05,22), "5/22/2024 12:00:00 AM"],
        ["{0:N}", "123456.78", "123456.78"]
    ];

    [Theory, MemberData(nameof(ValidFormattingInParameterBracketCases))]
    public void ReferenceExpressionHandlesValueWithFormattingInParameterBrackets(string input, string parameterValue, string expected)
    {
        var expr = ReferenceExpression.Create($"{input}", [new HostUrl("test")], [parameterValue]).ValueExpression;
        Assert.Equal(expected, expr);
    }

    [Theory]
    [InlineData("{0} {x}", "abc123", "abc123 {x}")]
    [InlineData("{x} {0}", "abc123", "{x} abc123")]
    [InlineData("{0} test {x}", "abc123", "abc123 test {x}")]
    [InlineData("{x} test {0}", "abc123", "{x} test abc123")]
    public void ReferenceExpressionHandlesValueWithBothParameterAndNonParameterBrackets(string input, string parameterValue, string expected)
    {
        var expr = ReferenceExpression.Create($"{input}", [new HostUrl("test")], [parameterValue]).ValueExpression;
        Assert.Equal(expected, expr);
    }

    [Fact]
    public void ReferenceExpressionHandlesValueWithoutBrackets()
    {
        var s = "Test";
        var expr = ReferenceExpression.Create($"{s}").ValueExpression;
        Assert.Equal("Test", expr);
    }
}
