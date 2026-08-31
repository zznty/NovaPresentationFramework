namespace Nova.Serialization.Tests;

public sealed class JournalValueSerializerTests
{
    [Theory]
    [InlineData(3.14)]
    [InlineData(42)]
    [InlineData("hello")]
    [InlineData(true)]
    public void RoundTrip_Primitives(object value)
    {
        Assert.True(TryRoundTrip(value, out object? restored));
        Assert.Equal(value, restored);
    }

    [Fact]
    public void RoundTrip_DateTime_Decimal_Enum()
    {
        Assert.True(TryRoundTrip(new DateTime(2026, 8, 26, 12, 30, 0, DateTimeKind.Utc), out object? dt));
        Assert.Equal(new DateTime(2026, 8, 26, 12, 30, 0, DateTimeKind.Utc), dt);

        Assert.True(TryRoundTrip(1.5m, out object? dec));
        Assert.Equal(1.5m, dec);

        Assert.True(TryRoundTrip(DayOfWeek.Friday, out object? day));
        Assert.Equal(DayOfWeek.Friday, day);
    }

    [Fact]
    public void RoundTrip_Struct_WithPublicProperties()
    {
        var value = new SampleStruct { X = 7, Y = -2 };
        Assert.True(TryRoundTrip(value, out object? restored));
        _ = Assert.IsType<SampleStruct>(restored);
        Assert.Equal(value, restored);
    }

    [Fact]
    public void TryRead_RejectsForeignPayload()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        Assert.False(JournalValueSerializer.TryRead(stream, out _));
    }

    private static bool TryRoundTrip(object value, out object? restored)
    {
        using var stream = new MemoryStream();
        if (!JournalValueSerializer.TryWrite(stream, value))
        {
            restored = null;
            return false;
        }

        stream.Position = 0;
        return JournalValueSerializer.TryRead(stream, out restored);
    }
}
