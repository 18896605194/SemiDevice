using System.Xml;
using System.Xml.Linq;
using xyz.Configs;
using xyz.Modules;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Tools;

namespace xyz.Service;

public partial class ChamberService
{
    public async Task<RpcResponse> GetLayoutAsync(RpcRequest request)
    {
        if (!request.TryGet<string>(RpcParams.Module, out var module) || string.IsNullOrWhiteSpace(module))
            return await InvalidRequest();
        var chamber = FindModule<IChamber>(module);
        if (chamber is null) return await ModuleNotFound(module);
        try
        {
            // 仅由已装配模块的路径生成文件名，不接受客户端提供的文件路径。
            string root = chamber.FullPath;
            string file = Path.Combine(MachineLoader.ConfigDirectory, "ChamberLayouts", "machine", Uri.EscapeDataString(root) + ".xml");
            var data = new ChamberLayoutDto { Module = chamber.Name, ComponentRoot = root };
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length > 1_000_000) throw new InvalidDataException("Layout is too large.");
                data.Xml = await File.ReadAllTextAsync(file);
                using var reader = XmlReader.Create(new StringReader(data.Xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 });
                var document = XDocument.Load(reader);
                if (document.Root?.Name != "ChamberLayout" || (string?)document.Root.Attribute("Version") != "3"
                    || (string?)document.Root.Attribute("ComponentRoot") != root)
                    throw new InvalidDataException("Layout does not match the chamber.");
            }
            return RpcResponse.Ok(JsonHelper.Serialize(data));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or XmlException)
        {
            return RpcResponse.Fail(ErrorCodes.ChamberLayoutInvalid, [module, failure.Message]);
        }
    }
}
