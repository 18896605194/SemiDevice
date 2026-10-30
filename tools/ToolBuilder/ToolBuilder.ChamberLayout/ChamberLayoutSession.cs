namespace ChamberLayoutEditor;

internal sealed class ChamberLayoutSession(ChamberLayoutDocument configuration)
{
    public ChamberLayoutDocument Configuration { get; } = configuration;
    public string Xml { get; set; } = configuration.ToXml();
    public string SavedXml { get; set; } = configuration.ToXml();
    public List<string> History { get; } = [configuration.ToXml()];
    public Stack<string> Redo { get; } = [];
    public string? SelectedId { get; set; }
}
