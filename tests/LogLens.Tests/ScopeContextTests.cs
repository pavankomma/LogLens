using Xunit;

namespace LogLens.Tests;

public class ScopeContextTests
{
    [Fact]
    public static async Task Scopes_flow_with_async_and_pop_on_dispose()
    {
        Assert.True(ScopeContext.IsEmpty);
        using (ScopeContext.Push("A", 1))
        {
            await Task.Yield();
            var d = new Dictionary<string, object?>();
            ScopeContext.CopyTo(d);
            Assert.Equal(1, d["A"]);
            await Task.Run(() =>
            {
                var inner = new Dictionary<string, object?>();
                ScopeContext.CopyTo(inner);
                Assert.Equal(1, inner["A"]);
            });
        }

        Assert.True(ScopeContext.IsEmpty);
    }

    [Fact]
    public static void Out_of_order_dispose_is_ignored()
    {
        var outer = ScopeContext.Push("O", 1);
        var inner = ScopeContext.Push("I", 2);
        outer.Dispose(); // not innermost: ignored
        var d = new Dictionary<string, object?>();
        ScopeContext.CopyTo(d);
        Assert.Equal(2, d.Count);
        inner.Dispose();
        inner.Dispose(); // idempotent
        outer.Dispose();
        Assert.True(ScopeContext.IsEmpty);
    }

    [Fact]
    public static void Empty_name_throws() => Assert.Throws<ArgumentException>(() => ScopeContext.Push("", 1));
}
