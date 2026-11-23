using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace KashFlow;

/// <summary>
/// A KashFlow <c>ArrayOfAnyType</c> list. Items are written as <c>anyType</c> elements with
/// <c>xsi:type</c> set to <typeparamref name="T"/>, which is what the service requires.
/// </summary>
[CollectionBuilder(typeof(KashFlowAnyList), nameof(KashFlowAnyList.Create))]
public sealed class KashFlowAnyList<T> : List<T>, IXmlSerializable
{
    private const string Namespace = "KashFlow";

    public KashFlowAnyList()
    {
    }

    public KashFlowAnyList(int capacity)
        : base(capacity)
    {
    }
    private static readonly XNamespace Service = Namespace;
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly ConcurrentDictionary<Type, XmlSerializer> Serializers = new();

    public XmlSchema? GetSchema() => null;

    public void ReadXml(XmlReader reader)
    {
        reader.MoveToContent();
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.ReadStartElement();
        while (true)
        {
            reader.MoveToContent();
            if (reader.NodeType is XmlNodeType.EndElement or XmlNodeType.None)
                break;
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            using (var subtree = reader.ReadSubtree())
            {
                subtree.MoveToContent();
                Add(ReadItem(XElement.Load(subtree)));
            }

            // ReadSubtree leaves the outer reader on the item's end element.
            if (reader.NodeType == XmlNodeType.EndElement)
                reader.Read();
        }

        if (reader.NodeType == XmlNodeType.EndElement)
            reader.ReadEndElement();
    }

    public void WriteXml(XmlWriter writer)
    {
        foreach (var item in this)
        {
            if (item is null)
            {
                writer.WriteStartElement("anyType", Namespace);
                writer.WriteAttributeString("xsi", "nil", Xsi.NamespaceName, "true");
                writer.WriteEndElement();
                continue;
            }

            var element = Serialize(item);
            element.Name = Service + "anyType";
            element.SetAttributeValue(Xsi + "type", typeof(T).Name);
            element.WriteTo(writer);
        }
    }

    private static T ReadItem(XElement element)
    {
        if (element.Attribute(Xsi + "nil")?.Value == "true")
            return default!;

        var declared = element.Attribute(Xsi + "type")?.Value;
        if (!string.IsNullOrEmpty(declared))
        {
            var local = declared.Contains(':', StringComparison.Ordinal)
                ? declared[(declared.LastIndexOf(':') + 1)..]
                : declared;
            if (!local.Equals(typeof(T).Name, StringComparison.Ordinal))
                throw new InvalidOperationException($"KashFlow sent {local} where {typeof(T).Name} was required.");
            element.Attribute(Xsi + "type")!.Remove();
        }

        element.Name = Service + typeof(T).Name;
        using var reader = element.CreateReader();
        if (Serializer().Deserialize(reader) is not T item)
            throw new InvalidOperationException($"KashFlow returned an empty {typeof(T).Name}.");

        return item;
    }

    private static XElement Serialize(T item)
    {
        var buffer = new StringWriter();
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("", Namespace);
        Serializer().Serialize(buffer, item, namespaces);
        return XElement.Parse(buffer.ToString());
    }

    private static XmlSerializer Serializer()
        => Serializers.GetOrAdd(typeof(T), static type => new XmlSerializer(
            type,
            new XmlRootAttribute(type.Name) { Namespace = Namespace }));
}

/// <summary>Creates <see cref="KashFlowAnyList{T}"/> instances from collection expressions.</summary>
public static class KashFlowAnyList
{
    public static KashFlowAnyList<T> Create<T>(ReadOnlySpan<T> items)
    {
        var list = new KashFlowAnyList<T>(items.Length);
        foreach (var item in items)
            list.Add(item);
        return list;
    }
}
