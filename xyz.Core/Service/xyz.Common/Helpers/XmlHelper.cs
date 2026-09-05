using System.Xml.Serialization;

namespace xyz.Common.Helpers;

/// <summary>
/// XML 序列化/反序列化通用辅助类。
/// </summary>
public static class XmlHelper
{
    /// <summary>
    /// 从文件反序列化为对象。
    /// </summary>
    public static T? Deserialize<T>(string filePath) where T : class
    {
        var serializer = new XmlSerializer(typeof(T));
        using var stream = File.OpenRead(filePath);
        return serializer.Deserialize(stream) as T;
    }

    /// <summary>
    /// 从流反序列化为对象。
    /// </summary>
    public static T? Deserialize<T>(Stream stream) where T : class
    {
        var serializer = new XmlSerializer(typeof(T));
        return serializer.Deserialize(stream) as T;
    }

    /// <summary>
    /// 序列化对象到文件。
    /// </summary>
    public static void Serialize<T>(string filePath, T value) where T : class
    {
        var serializer = new XmlSerializer(typeof(T));
        using var stream = File.Create(filePath);
        serializer.Serialize(stream, value);
    }
}
