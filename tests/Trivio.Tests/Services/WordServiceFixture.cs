using Trivio.Services;

public class WordServiceFixture
{
    public WordService Service { get; private set; }
    public WordServiceFixture()
    {
        // Create a temporary file with test words
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(tempRoot, "data"));
        var csvPath = Path.Combine(tempRoot, "data", "tr_words.csv");
        File.WriteAllText(csvPath, "example\nanother\n");

        // Initialize the service with the test file path
        Service = new WordService(csvPath);
    }
}