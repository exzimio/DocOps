using System.Reflection;
using System.Text;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// The realistic sample documents kept in <c>samples/</c> at the repository root, embedded at build
/// time. They are laid out the way real invoices and contracts are, so the demonstration AI has
/// genuine text to read rather than the stored metadata written back out as text.
/// </summary>
public static class SampleDocuments
{
    public static string Read(string fileName)
    {
        string resource = $"DocOps.Samples.{fileName}";

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Sample document '{fileName}' is not embedded. Check samples/ and the project file.");

        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
