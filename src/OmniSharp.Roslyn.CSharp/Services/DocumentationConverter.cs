using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using OmniSharp.Models.TypeLookup;

namespace OmniSharp.Roslyn.CSharp.Services.Documentation
{
    public static class DocumentationConverter
    {
        /// <summary>
        /// Converts the xml documentation string into a plain text string.
        /// </summary>
        public static string ConvertDocumentation(string xmlDocumentation, string lineEnding)
        {
            if (string.IsNullOrEmpty(xmlDocumentation))
                return string.Empty;

            var reader = new StringReader("<docroot>" + xmlDocumentation + "</docroot>");
            using (var xml = XmlReader.Create(reader))
            {
                var ret = new StringBuilder();

                try
                {
                    xml.Read();
                    string elementName = null;
                    do
                    {
                        if (xml.NodeType == XmlNodeType.Element)
                        {
                            elementName = xml.Name.ToLowerInvariant();
                            switch (elementName)
                            {
                                case "filterpriority":
                                    xml.Skip();
                                    break;
                                case "remarks":
                                    ret.Append(lineEnding);
                                    ret.Append("Remarks:");
                                    ret.Append(lineEnding);
                                    break;
                                case "example":
                                    ret.Append(lineEnding);
                                    ret.Append("Example:");
                                    ret.Append(lineEnding);
                                    break;
                                case "exception":
                                    ret.Append(lineEnding);
                                    ret.Append(GetCref(xml["cref"]).TrimEnd());
                                    ret.Append(": ");
                                    break;
                                case "returns":
                                    ret.Append(lineEnding);
                                    ret.Append("Returns: ");
                                    break;
                                case "see":
                                    ret.Append(GetCref(xml["cref"]));
                                    ret.Append(xml["langword"]);
                                    break;
                                case "seealso":
                                    ret.Append(lineEnding);
                                    ret.Append("See also: ");
                                    ret.Append(GetCref(xml["cref"]));
                                    break;
                                case "paramref":
                                    ret.Append(xml["name"]);
                                    ret.Append(" ");
                                    break;
                                case "typeparam":
                                    ret.Append(lineEnding);
                                    ret.Append("<");
                                    ret.Append(TrimMultiLineString(xml["name"], lineEnding));
                                    ret.Append(">: ");
                                    break;
                                case "param":
                                    ret.Append(lineEnding);
                                    ret.Append(TrimMultiLineString(xml["name"], lineEnding));
                                    ret.Append(": ");
                                    break;
                                case "value":
                                    ret.Append(lineEnding);
                                    ret.Append("Value: ");
                                    ret.Append(lineEnding);
                                    break;
                                case "br":
                                case "para":
                                    ret.Append(lineEnding);
                                    break;
                            }
                        }
                        else if (xml.NodeType == XmlNodeType.Text)
                        {
                            if (elementName == "code")
                            {
                                ret.Append(xml.Value);
                            }
                            else
                            {
                                ret.Append(TrimMultiLineString(xml.Value, lineEnding));
                            }
                        }
                    } while (xml.Read());
                }
                catch (Exception)
                {
                    return xmlDocumentation;
                }
                return ret.ToString();
            }
        }

        private static string TrimMultiLineString(string input, string lineEnding)
        {
            var lines = input.Split(new string[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(lineEnding, lines.Select(l => l.TrimStart()));
        }

        private static string GetCref(string cref)
        {
            if (cref == null || cref.Trim().Length == 0)
            {
                return "";
            }
            if (cref.Length < 2)
            {
                return cref;
            }
            if (cref.Substring(1, 1) == ":")
            {
                return cref.Substring(2, cref.Length - 2) + " ";
            }
            return cref + " ";
        }

        public static DocumentationComment GetStructuredDocumentation(
            string xmlDocumentation,
            string externalDocumentation,
            string lineEnding)
        {
            if (!externalDocumentation.Contains("<summary>"))
                externalDocumentation = "<summary>" + externalDocumentation + "</summary>";
            return DocumentationComment.From(xmlDocumentation + lineEnding + externalDocumentation, lineEnding);
        }

        public static DocumentationComment GetStructuredDocumentation(
            ISymbol symbol,
            string folderForExternalAnnotations,
            string lineEnding = "\n")
        {
            switch (symbol)
            {
                case IParameterSymbol parameter:
                    return new DocumentationComment(summaryText: GetParameterDocumentation(parameter, lineEnding));
                case ITypeParameterSymbol typeParam:
                    return new DocumentationComment(summaryText: GetTypeParameterDocumentation(typeParam, lineEnding));
                case IAliasSymbol alias:
                    return new DocumentationComment(summaryText: GetAliasDocumentation(alias, lineEnding));
                default:
                    return GetStructuredDocumentation(
                        symbol.GetDocumentationCommentXml(),
                        GetExternalDocumentation(symbol, lineEnding, folderForExternalAnnotations),
                        lineEnding);
            }
        }

        private static string GetExternalDocumentation(
            ISymbol symbol,
            string lineEnding,
            string folderForExternalAnnotations)
        {
            var pathToExternalAnnotations = folderForExternalAnnotations
                + System.IO.Path.DirectorySeparatorChar + symbol.ContainingAssembly.Name + ".ExternalAnnotations.xml";
            if (!System.IO.File.Exists(pathToExternalAnnotations))
            {
                return lineEnding + pathToExternalAnnotations + " not found";
            }

            var ttt = symbol.ToDisplayString() + lineEnding;

            XmlDocument xDoc = new XmlDocument();
            try
            {
                xDoc.Load(pathToExternalAnnotations);
                foreach (XmlNode element in xDoc.DocumentElement)
                {
                    if (element?.Attributes?.GetNamedItem("name")?.Value == symbol.ToDisplayString())
                        ttt += element.InnerXml;
                }
            }
            catch (Exception e)
            {
                return "Exception: " + e.Message;
            }
            ttt += lineEnding + "Assembly: " + symbol.ContainingAssembly;
            return ttt;
        }

        private static string GetParameterDocumentation(
            IParameterSymbol parameter,
            string folderForExternalAnnotations,
            string lineEnding = "\n")
        {
            var contaningSymbolDef = parameter.ContainingSymbol.OriginalDefinition;
            var doc = contaningSymbolDef.GetDocumentationCommentXml();
            var external = GetExternalDocumentation(contaningSymbolDef, lineEnding, folderForExternalAnnotations);
            return GetStructuredDocumentation(doc, external, lineEnding)
                    .GetParameterText(parameter.Name);
        }

        private static string GetTypeParameterDocumentation(
            ITypeParameterSymbol typeParam,
            string folderForExternalAnnotations,
            string lineEnding = "\n")
        {
            var contaningSymbol = typeParam.ContainingSymbol;
            var external = GetExternalDocumentation(contaningSymbol, lineEnding, folderForExternalAnnotations);
            return GetStructuredDocumentation(contaningSymbol.GetDocumentationCommentXml(), external, lineEnding)
                    .GetTypeParameterText(typeParam.Name);
        }

        private static string GetAliasDocumentation(
            IAliasSymbol alias,
            string folderForExternalAnnotations,
            string lineEnding = "\n")
        {
            var target = alias.Target;
            var external = GetExternalDocumentation(target, lineEnding, folderForExternalAnnotations);
            return GetStructuredDocumentation(target.GetDocumentationCommentXml(), external, lineEnding).SummaryText;
        }
    }
}
