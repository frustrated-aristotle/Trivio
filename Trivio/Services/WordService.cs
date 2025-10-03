using Microsoft.AspNetCore.Hosting; // IWebHostEnvironment için
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Trivio.Services
{
    public class WordService : IWordService
    {
        private readonly string _filePath;
        public WordService(IWebHostEnvironment env)
        {
            _filePath = Path.Combine(env.WebRootPath, "data", "tr_words.csv");
        }

        public async Task<List<string>> GetRandomWords(int count)
        {
            // Guard: non-positive requests yield empty list
            if (count <= 0)
            {
                return new List<string>();
            }

            // Load words and ensure uniqueness
            var allWords = await ReadAllWordsFromFileAsync();
            if (allWords == null || allWords.Count == 0)
            {
                return new List<string>();
            }

            var uniqueWords = allWords
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueWords.Count == 0)
            {
                return new List<string>();
            }

            // If requesting more than available, just return all in a random order
            if (count >= uniqueWords.Count)
            {
                ShuffleInPlace(uniqueWords);
                return uniqueWords;
            }

            // Shuffle then take the requested count
            ShuffleInPlace(uniqueWords);
            return uniqueWords.Take(count).ToList();
        }

        private static void ShuffleInPlace<T>(IList<T> list)
        {
            // Fisher–Yates using cryptographic RNG for better randomness across app restarts
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                if (j == i) continue;
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public async Task<List<string>> ReadAllWordsFromFileAsync()
        {
            if (!File.Exists(_filePath))
            {
                System.Console.WriteLine($"WARNING: Word file not found at {_filePath}");
                return new List<string>();
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(_filePath);
                return lines
                       .Where(line => !string.IsNullOrWhiteSpace(line))
                       .Select(line => line.Trim().ToLower())
                       .ToList();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"ERROR reading word file: {ex.Message}");
                return new List<string>();
            }
        }

        public List<char> GetRandomConsonants(int count)
        {
            // Turkish consonants (excluding vowels: a, e, i, ı, o, ö, u, ü)
            var turkishConsonants = new List<char>
            {
                'b', 'c', 'ç', 'd', 'f', 'g', 'ğ', 'h', 'j', 'k', 'l', 'm', 'n', 'p', 'r', 's', 'ş', 't', 'v', 'y', 'z'
            };

            var shuffled = turkishConsonants.ToList();
            ShuffleInPlace(shuffled);
            
            return shuffled.Take(count).ToList();
        }

        public bool IsValidWord(string word, List<char> allowedConsonants)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            // Check if all consonants in the word are in the allowed list
            var wordConsonants = word.ToLowerInvariant()
                .Where(c => !IsVowel(c))
                .ToList();

            return wordConsonants.All(consonant => allowedConsonants.Contains(consonant));
        }

        public async Task<bool> WordExistsInDictionary(string word)
        {
            var allWords = await ReadAllWordsFromFileAsync();
            return allWords.Contains(word.ToLowerInvariant());
        }

        private static bool IsVowel(char c)
        {
            var vowels = new HashSet<char> { 'a', 'e', 'i', 'ı', 'o', 'ö', 'u', 'ü' };
            return vowels.Contains(char.ToLowerInvariant(c));
        }
    }
}