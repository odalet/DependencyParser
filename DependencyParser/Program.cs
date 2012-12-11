using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;

using Mono.Cecil;
using Mono.Cecil.Pdb;

namespace DependencyParser
{
    public class Program
    {
        private static readonly string appname = Assembly.GetExecutingAssembly().GetName().Name;
        private static readonly List<string> parsedAssemblies = new List<string>();
        private static readonly List<string> assembliesToParse = new List<string>();

        private static TextWriter outw = Console.Out;
        private static TextWriter errw = Console.Error;

        public static string AppName
        {
            get { return appname; }
        }

        public static int Main(string[] args)
        {
            return new Program().Run(args);
        }

        private int Run(string[] args)
        {
            var cmd = new CommandLineProcessor(args, outw);
            if (!cmd.Execute()) return 0;

            var assemblyFileName = CheckAssemblyFile(cmd.AssemblyFileName);
            if (string.IsNullOrEmpty(assemblyFileName)) return -1;

            var outputFileName = CheckOutputFile(cmd.OutputFileName);
            if (string.IsNullOrEmpty(outputFileName)) return -2;

            outw.WriteLine("Running analysis on assembly {0}", assemblyFileName);
            outw.WriteLine("Output report file is: {0}", outputFileName);

            AssemblyDefinition definition = null;
            try
            {
                definition = AssemblyDefinition.ReadAssembly(assemblyFileName);
            }
            catch (Exception ex)
            {
                errw.WriteError("Could not analyze assembly {0}; {1}", assemblyFileName, ex.Message);
                return -3;
            }

            using (var stream = new FileStream(outputFileName, FileMode.Create))
            using (var writer = new XmlTextWriter(stream, Encoding.UTF8))
            {
                writer.Formatting = Formatting.Indented;
                writer.WriteStartDocument();
                writer.WriteStartElement("Dependencies");
                writer.WriteAttributeString("name", definition.MainModule.Assembly.Name.Name);
                writer.WriteAttributeString("version", definition.MainModule.Assembly.Name.Version.ToString());

                RunAnalysis(writer, definition, assemblyFileName);

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            return 0;
        }

        private void RunAnalysis(XmlTextWriter writer, AssemblyDefinition definition, string assemblyName)
        {
            Analysis(writer, definition.MainModule, assemblyName, true);
            var targetFolder = Path.GetDirectoryName(assemblyName);

            while (assembliesToParse.Count > 0)
            {
                definition = null;
                var fullName = assembliesToParse.First();
                var assemblyNameDef = AssemblyNameReference.Parse(fullName);
                var name = assemblyNameDef.Name;

                // find that file
                var dllFile = new FileInfo(Path.Combine(targetFolder, name + ".dll"));
                var exeFile = new FileInfo(Path.Combine(targetFolder, name + ".exe"));
                FileInfo targetFile = null;

                if (dllFile.Exists)
                {
                    targetFile = dllFile;
                }
                else if (exeFile.Exists)
                {
                    targetFile = exeFile;
                }

                if (targetFile != null)
                {
                    definition = AssemblyDefinition.ReadAssembly(targetFile.FullName);

                    if (definition != null)
                    {
                        if (!definition.FullName.Equals(fullName))
                        {
                            Console.WriteLine("The existing file {0} doesn't match the fullName {1}, skip it", name, fullName);
                            assembliesToParse.Remove(fullName);
                            parsedAssemblies.Add(fullName);
                        }
                        else
                        {
                            Analysis(writer, definition.MainModule, targetFile.FullName, false);
                        }
                    }
                }
                else
                {
                    // how to do for the GAC?
                    //definition = AssemblyDefinition.ReadAssembly()
                    Console.WriteLine("Skip {0}... maybe in the GAC?", name);
                    assembliesToParse.Remove(fullName);
                    parsedAssemblies.Add(fullName);
                }
            }
        }

        #region Helpers

        private string CheckAssemblyFile(string input)
        {
            if (!Path.IsPathRooted(input))
                input = Path.Combine(Environment.CurrentDirectory, input);

            if (!File.Exists(input))
            {
                errw.WriteError(string.Format("File {0} could not be found.", input));
                return string.Empty;
            }

            return input;
        }

        private string CheckOutputFile(string input)
        {
            if (!Path.IsPathRooted(input))
                input = Path.Combine(Environment.CurrentDirectory, input);

            if (File.Exists(input))
            {
                // Delete it
                try
                {
                    File.Delete(input);
                }
                catch (Exception ex)
                {
                    errw.WriteError(string.Format("File {0} could not be deleted; {1}", input, ex.Message));
                    return string.Empty;
                }
            }
            else // Make sure the output directory exists
            {
                var outputDir = Path.GetDirectoryName(input);
                if (!Directory.Exists(outputDir))
                {
                    errw.WriteError(string.Format("Directory {0} could not be found.", outputDir));
                    return string.Empty;
                }
            }

            return input;
        }

        #endregion

        private static void Analysis(XmlTextWriter writer, ModuleDefinition module, string fullPath, bool withTypes)
        {
            try
            {
                module.ReadSymbols();

                var provider = new PdbReaderProvider();
                var reader = provider.GetSymbolReader(module, fullPath);
            }
            catch (FileNotFoundException fex)
            {
                // we don't want to fail on a missing pdb.
                // though we may place a breakpoint below.
                var debugException = fex;
            }

            Console.WriteLine("Parsing {0}", module.Name);
            writer.WriteStartElement("Assembly");
            writer.WriteAttributeString("name", module.Assembly.Name.Name);
            writer.WriteAttributeString("version", module.Assembly.Name.Version.ToString());
            writer.WriteStartElement("References");
            foreach (var item in module.AssemblyReferences)
            {
                writer.WriteStartElement("Reference");
                writer.WriteAttributeString("name", item.Name);
                writer.WriteAttributeString("fullName", item.FullName);
                writer.WriteAttributeString("version", item.Version.ToString());
                writer.WriteEndElement();

                if (!parsedAssemblies.Contains(item.FullName) && !assembliesToParse.Contains(item.FullName))
                {
                    assembliesToParse.Add(item.FullName);
                }
            }
            writer.WriteEndElement();

            if (withTypes)
            {
                writer.WriteStartElement("TypeReferences");
                foreach (var t in module.Types)
                {
                    ParseType(writer, t);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();

            if (assembliesToParse.Contains(module.Assembly.Name.FullName))
            {
                assembliesToParse.Remove(module.Assembly.Name.FullName);
            }

            parsedAssemblies.Add(module.Assembly.Name.FullName);
        }

        private static void ParseType(XmlTextWriter writer, TypeDefinition t)
        {
            // ignore generated types
            if (t.DeclaringType == null && t.Namespace.Equals(string.Empty))
            {
                return;
            }

            if (t.Name.StartsWith("<>"))
            {
                return;
            }

            foreach (var n in t.NestedTypes)
            {
                ParseType(writer, n);
            }

            Dictionary<string, IList<string>> cache = new Dictionary<string, IList<string>>();
            writer.WriteStartElement("From");
            writer.WriteAttributeString("fullname", t.FullName);

            foreach (var c in t.CustomAttributes)
            {
                AddDependency(writer, cache, t, c.AttributeType);
            }

            if (t.BaseType != null)
            {
                AddDependency(writer, cache, t, t.BaseType);
            }

            foreach (var i in t.Interfaces)
            {
                AddDependency(writer, cache, t, i);
            }

            foreach (var e in t.Events)
            {
                AddDependency(writer, cache, t, e.EventType);
            }

            foreach (var f in t.Fields)
            {
                AddDependency(writer, cache, t, f.FieldType);
            }

            foreach (var p in t.Properties)
            {
                AddDependency(writer, cache, t, p.PropertyType);
            }

            foreach (var m in t.Methods)
            {
                AddDependency(writer, cache, t, m.ReturnType);

                foreach (var p in m.Parameters)
                {
                    AddDependency(writer, cache, t, p.ParameterType);
                }

                if (m.Body != null)
                {
                    //m.Body.Instructions[0].SequencePoint.Document

                    foreach (var v in m.Body.Variables)
                    {
                        AddDependency(writer, cache, t, v.VariableType);
                    }

                    foreach (var e in m.Body.ExceptionHandlers)
                    {
                        if (e.CatchType != null)
                        {
                            AddDependency(writer, cache, t, e.CatchType);
                        }
                    }
                }
            }

            writer.WriteEndElement();
        }

        private static void AddDependency(
            XmlTextWriter writer, IDictionary<string, IList<string>> cache, TypeDefinition from, TypeReference to)
        {
            if (from.FullName.Equals(to.FullName))
            {
                return;
            }

            // ignore generic parameters
            if (to.IsGenericParameter)
            {
                return;
            }

            // ignore generated types, without namespace
            if (to.Namespace.Equals(string.Empty))
            {
                return;
            }

            if (to.IsArray)
            {
                to = to.GetElementType();
            }

            if (to.IsGenericInstance)
            {
                var generic = (GenericInstanceType)to;
                foreach (var a in generic.GenericArguments)
                {
                    AddDependency(writer, cache, from, a);
                }
                to = to.GetElementType();
            }

            // ignore types from .Net framework
            if (to.Scope.Name.Equals("mscorlib") || to.Scope.Name.StartsWith("System") || to.Scope.Name.StartsWith("Microsoft"))
            {
                return;
            }

            IList<string> toList;
            if (!cache.TryGetValue(from.FullName, out toList))
            {
                toList = new List<string>();
                cache.Add(from.FullName, toList);
            }

            if (toList.Contains(to.FullName))
            {
                return;
            }


            writer.WriteStartElement("To");
            writer.WriteAttributeString("fullname", to.FullName);
            if (to.Scope is ModuleDefinition)
            {
                writer.WriteAttributeString("assemblyname", ((ModuleDefinition)to.Scope).Assembly.Name.Name);
                writer.WriteAttributeString("assemblyversion", ((ModuleDefinition)to.Scope).Assembly.Name.Version.ToString());
            }
            else if (to.Scope is AssemblyNameReference)
            {
                writer.WriteAttributeString("assemblyname", ((AssemblyNameReference)to.Scope).Name);
                writer.WriteAttributeString("assemblyversion", ((AssemblyNameReference)to.Scope).Version.ToString());
            }

            writer.WriteEndElement();

            toList.Add(to.FullName);
        }
    }
}
