namespace Trivio.Services
{
    public interface IWordService
    {
        Task<List<string>> ReadAllWordsFromFileAsync();
        Task<List<string>> GetRandomWords(int count);
        List<char> GetRandomConsonants(int count);
        bool HasAllowedConsonants(string word, List<char> allowedConsonants);
        Task<bool> WordExistsInDictionary(string word);
    }
}
