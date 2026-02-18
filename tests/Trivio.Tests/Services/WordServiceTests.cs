using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Trivio.Services;

namespace Trivio.Tests.Services;

public class WordServiceTests : IClassFixture<WordServiceFixture>
{

    private readonly WordService _service;

    public WordServiceTests(WordServiceFixture fixture)
    {
        _service = fixture.Service;
    }

    #region GetRandomConsonants Tests
    //Test cases for GetRandomConsonants method
    [Fact]
    public void GetRandomConsonants_ReturnsOnlyConsonants()
    {
        // Arrange
        var consonantCount = 21;
        var vowels = new HashSet<char> { 'a', 'e', 'i', 'ı', 'o', 'ö', 'u', 'ü' };
        // Act
        var result = _service.GetRandomConsonants(consonantCount);
        // Assert
        Assert.NotNull(result);
        Assert.All(result, c=> Assert.DoesNotContain(char.ToLowerInvariant(c), vowels));
    }
    [Fact]
    public void GetRandomConsonants_ReturnsRequestedCount_WhenCountPositive()
    {
        // Arrange
        int count = 4;
        // Act
        var result = _service.GetRandomConsonants(count);
        // Assert
        Assert.NotNull(result);
        Assert.Equal(count, result.Count);
    }
    [Fact]
    public void GetRandomConsonants_ReturnsAtMostAllConsonants_WhenCountTooLarge()
    {
        // Arrange
        int count = 100;
        int alphabetConsonantCount = 21; 
        // Act
        var result = _service.GetRandomConsonants(count);
        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count <= alphabetConsonantCount); 
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetRandomConsonants_ReturnsEmptyList_WhenCountIsZeroOrNegative(int count)
    {
        // Arrange

        // Act
        var result = _service.GetRandomConsonants(count);
        // Result
        Assert.NotNull(result);
        Assert.Empty(result);
    }
    #endregion

    #region HasAllowedConsonants Tests
    //Test cases for HasAllowedConsonants method
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void HasAllowedConsonants_ReturnsFalse_WhenWordNullOrWhitespace(string word)
    {
        //This should be a theory test with null and whitespace
        // Arrange
        //A list of consonants player can use to build a word
        var allowedConsonants = new List<char> { 'b', 'c' , 'd' };
        // Act
        var result = _service.HasAllowedConsonants(word, allowedConsonants); 
        // Assert
        Assert.False(result);
    }
    
    [Fact]
    public void HasAllowedConsonants_ReturnsTrue_WhenAllConsonantsAllowed()
    {
        // Arrange
        var allowedConsonants = new List<char> { 'b', 'c', 'd' };
        
        // Mixed case to test case insensitivity
        var word = "Bad"; 
        // Act
        var result = _service.HasAllowedConsonants(word, allowedConsonants);
        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasAllowedConsonants_ReturnsFalse_WhenSomeConsonantsNotAllowed()
    {
        // Arrange
        var allowedConsonants = new List<char> { 'b', 'c' };
        var word = "bad";
        // Act
        var result = _service.HasAllowedConsonants(word, allowedConsonants);
        // Assert
        Assert.False(result);
    }
    #endregion
    
    #region ReadAllWordsFromFileAsync Tests
    //Test cases for ReadAllWordsFromFileAsync method
    [Fact]
    public async Task ReadAllWordsFromFileAsync_ReturnsEmpty_WhenFileMissing()
    {
        // Arrange
        var nonExistentPath = "/tmp/nonexistent_file_" + Guid.NewGuid() + ".csv";
        var service = new WordService(nonExistentPath);
    
        // Act
        var result = await service.ReadAllWordsFromFileAsync();
    
        // Assert
        Assert.Empty(result);
    }

    public static IEnumerable<object[]> GetFileContentTestData() =>        new List<object[]>
    {
        new object[] { new List<string> { "word ", "Word1" } },
        new object[] { new List<string> { "word2", "word3" } }
    };
    [Theory]
    [MemberData(nameof(GetFileContentTestData))]
    public async Task ReadAllWordsFromFileAsync_ReturnsTrimmedLowercaseWords(List<string> lines)
    {
        // Arrange
        var tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(tempFilePath, lines);
            var service = new WordService(tempFilePath);
            // Act           
            var result = await service.ReadAllWordsFromFileAsync();
            // Assert
            Assert.All(result, word=>
            {
                Assert.Equal(word, word.Trim().ToLower());
            });
            Assert.NotEmpty(result);}   
        finally
        {
            File.Delete(tempFilePath);
        }
    }
    [Fact]
    public async Task ReadAllWordsFromFileAsync_SkipsBlankLines()
    {
        // Arrange
        var tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(tempFilePath, new[] { "", "word1", " ", "word2", "" });
            var service = new WordService(tempFilePath);
            // Act
            var result = await service.ReadAllWordsFromFileAsync();
            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains("word1", result);
            Assert.Contains("word2", result);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
    }
    #endregion

    #region WordExistsInDictionary Tests
    //Test cases for WordExistsInDictionary method
    [Fact]
    public async Task WordExistsInDictionary_ReturnsTrue_WhenWordExists()
    {
        // Arrange
        var word = "Example";
        // Act
        var result = await _service.WordExistsInDictionary(word);
        // Assert
        Assert.True(result);
    }
    [Theory]
    [InlineData("elma", true)]       
    [InlineData("ELMA", true)]      
    [InlineData("ElMa", true)]       
    [InlineData("armut", false)]   
    [InlineData("elma ", false)]
    public async Task WordExistsInDictionary_ShouldValidateCorrectly(string searchWord, bool expected)
    {
        // Arrange
        var mockWords = new List<string> { "elma", "muz" };
        var tempFilePath = Path.GetTempFileName();
        bool result;
        try
        {
            await File.WriteAllLinesAsync(tempFilePath, mockWords);
            var service = new WordService(tempFilePath);
            // Act
            result = await service.WordExistsInDictionary(searchWord);
        }
        finally
        {
            File.Delete(tempFilePath);
        }
        // Assert
        Assert.Equal(expected, result);
    }
    #endregion    
    [Fact]
    public void LoadWords_Returns_NonEmptyList_Placeholder()
    {
        // Arrange
        // TODO: Initialize WordService (mock dependencies if needed)

        // Act
        // TODO: call the method you want to test

        // Assert
        Assert.True(true); // replace with real assertions
    }

    [Fact]
    public void HasAllowedConsonants_AllConsonantsInList_ReturnsTrue()
    {
        //Arrange
        var allowedConsonants = new List<char> { 'b', 'c', 'd' };
        var word = "bad";
        //Act
        var result = _service.HasAllowedConsonants(word, allowedConsonants);
        //Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WordExistsInDictionary_ExistingWord_ReturnsTrue()
    {
        // Arrange
        // Act
        var word = "Example";
        var exists = await _service.WordExistsInDictionary(word);

        // Assert
        Assert.True(exists);
    }
}
