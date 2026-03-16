namespace EmbeddedSourceTestLibrary;

public class EmbeddedSourceSample
{
    // Embedded source marker comment
    public string GetValue()
    {
        const string marker = "EMBEDDED-SOURCE-MARKER";
        return marker;
    }
}
