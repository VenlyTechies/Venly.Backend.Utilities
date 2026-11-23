using Venly.Database.Helper;
namespace Venly.Backend.Utilities.Tests.Database;
public class SnakeCaseEnumConverterTests
{
    public enum Sample { Draft, PendingVerification, UShape }
    [Theory]
    [InlineData(Sample.Draft, "draft")]
    [InlineData(Sample.PendingVerification, "pending_verification")]
    [InlineData(Sample.UShape, "u_shape")]
    public void An_enum_round_trips_through_lower_snake_case_text(Sample value, string text)
    {
        Assert.Equal(text, SnakeCaseEnumConverter<Sample>.ToText(value));
        Assert.Equal(value, SnakeCaseEnumConverter<Sample>.FromText(text));
    }
    [Fact]
    public void The_check_list_covers_every_member_and_an_unknown_text_throws()
    {
        Assert.Equal(["draft", "pending_verification", "u_shape"], SnakeCaseEnumConverter<Sample>.AllTexts());
        Assert.Throws<InvalidOperationException>(() => SnakeCaseEnumConverter<Sample>.FromText("nope"));
    }
}
