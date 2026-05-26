using octo_fiesta.Services.Common;

namespace octo_fiesta.Tests;

public class StringNormalizerTests
{
    [Fact]
    public void NormalizeForComparison_WithCurlyApostrophe_ReturnsNormalizedString()
    {
        // Arrange
        var input = "The Craving (Jenna‘s Version)"; // Curly apostrophe
        var expected = "The Craving (Jenna's Version)"; // Straight apostrophe

        // Act
        var result = StringNormalizer.NormalizeForComparison(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeForComparison_WithBacktick_ReturnsNormalizedString()
    {
        // Arrange
        var input = "The Craving (Jenna`s Version)";
        var expected = "The Craving (Jenna's Version)";

        // Act
        var result = StringNormalizer.NormalizeForComparison(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeForComparison_WithCurlyDoubleQuotes_ReturnsNormalizedString()
    {
        // Arrange
        var input = "“Hello World”"; // Curly double quotes
        var expected = "\"Hello World\""; // Straight double quotes

        // Act
        var result = StringNormalizer.NormalizeForComparison(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeForComparison_WithLeftSingleQuotationMark_ReturnsNormalizedString()
    {
        // Arrange
        var input = "‘Hello"; // Left single quotation mark
        var expected = "'Hello"; // Straight apostrophe

        // Act
        var result = StringNormalizer.NormalizeForComparison(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeForComparison_WithNoSpecialQuotes_ReturnsUnchanged()
    {
        // Arrange
        var input = "Normal Song Title";

        // Act
        var result = StringNormalizer.NormalizeForComparison(input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void NormalizeForComparison_WithEmptyString_ReturnsEmptyString()
    {
        // Arrange
        var input = "";

        // Act
        var result = StringNormalizer.NormalizeForComparison(input);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeForComparison_WithNull_ReturnsEmptyString()
    {
        // Arrange
        string? input = null;

        // Act
        var result = StringNormalizer.NormalizeForComparison(input);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void CreateComparisonKey_WithMixedCase_ReturnsDistinctKeys()
    {
        var key1 = StringNormalizer.CreateComparisonKey("It'S A Song");
        var key2 = StringNormalizer.CreateComparisonKey("it's a song");

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void CreateComparisonKey_WithDifferentQuotes_ReturnsSameKey()
    {
        // Arrange
        var input1 = "It's"; // Straight apostrophe
        var input2 = "It’s"; // Curly apostrophe (U+2019)
        var input3 = "It`s"; // Backtick

        // Act
        var key1 = StringNormalizer.CreateComparisonKey(input1);
        var key2 = StringNormalizer.CreateComparisonKey(input2);
        var key3 = StringNormalizer.CreateComparisonKey(input3);

        // Assert
        Assert.Equal(key1, key2);
        Assert.Equal(key1, key3);
    }

    [Fact]
    public void CreateSongTitleDedupeKey_StripsTrailingFeatParens()
    {
        Assert.Equal("Boi", StringNormalizer.CreateSongTitleDedupeKey("Boi (feat. Butch Dawson)"));
    }

    [Fact]
    public void CreateSongTitleDedupeKey_StripsTrailingFeatBrackets()
    {
        Assert.Equal(
            "Praise The Lord (Da Shine)",
            StringNormalizer.CreateSongTitleDedupeKey("Praise The Lord (Da Shine) [feat. Skepta]"));
    }

    [Fact]
    public void CreateSongTitleDedupeKey_LeavesRemasteredSuffix()
    {
        Assert.Equal("Song (Remastered)", StringNormalizer.CreateSongTitleDedupeKey("Song (Remastered)"));
    }

    [Fact]
    public void CreateSongTitleDedupeKey_MatchesPlainAndFeatTitle()
    {
        Assert.Equal(
            StringNormalizer.CreateSongTitleDedupeKey("Boi"),
            StringNormalizer.CreateSongTitleDedupeKey("Boi (feat. Butch Dawson)"));
    }

    [Fact]
    public void CreateSongTitleDedupeKey_WithMixedCase_ReturnsDistinctKeys()
    {
        Assert.NotEqual(
            StringNormalizer.CreateSongTitleDedupeKey("Boi"),
            StringNormalizer.CreateSongTitleDedupeKey("boi"));
    }
}
