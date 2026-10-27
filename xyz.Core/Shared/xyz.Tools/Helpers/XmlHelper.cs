using System.Xml.Serialization;

namespace xyz.Tools;

public static class XmlHelper
{
    public static T? Deserialize<T>(string filePath) where T : class
    {
        var serializer = new XmlSerializer(typeof(T));
        using var stream = File.OpenRead(filePath);
        return serializer.Deserialize(stream) as T;
    }

    public static T? Deserialize<T>(Stream stream) where T : class
    {
        var serializer = new XmlSerializer(typeof(T));
        return serializer.Deserialize(stream) as T;
    }

    public static void Serialize<T>(string filePath, T value) where T : class
    {
        var serializer = new XmlSerializer(typeof(T));
        using var stream = File.Create(filePath);
        serializer.Serialize(stream, value);
    }
}
