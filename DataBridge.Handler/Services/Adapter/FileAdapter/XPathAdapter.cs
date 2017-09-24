﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using DataBridge.Extensions;
using DataBridge.Formatters;
using DataBridge.Helper;
using MyXPathReader;

namespace DataBridge
{
    public class XPathAdapter
    {
        private FormatterBase formatter = new XPathToDataTableFormatter();

        public FormatterBase Formatter
        {
            get { return this.formatter; }
            set { this.formatter = value; }
        }

        public static string[] XpathParts(string xpath)
        {
            if (string.IsNullOrEmpty(xpath))
            {
                return new string[0];
            }

            return xpath.Trim('/').Split('/');
        }

        public IEnumerable<object> ReadData(string file, string xPath, int maxRowsToRead)
        {
            string rowGrouper = "";

            XPathReader xpr = new XPathReader(file, xPath);

            while (xpr.ReadUntilMatch())
            {
                string xml = xpr.ReadOuterXml();
                Debug.WriteLine(xml);
            }

            using (XmlReader xmlReader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
            {
                foreach (var path in XpathParts(xPath))
                {
                    if (!xmlReader.SkipToElement(path))
                    {
                        throw new InvalidOperationException("XML element " + path + " was not found.");
                    }
                }

                DataSet dataSet = new DataSet();
                int readedRows = 0;

                while (xmlReader.Read())
                {
                    if (xmlReader.Name.Equals(rowGrouper) && (xmlReader.NodeType == XmlNodeType.Element))
                    {
                        var rowElement = (XElement)XElement.ReadFrom(xmlReader);
                        string xml = rowElement.ToStringOrEmpty();

                        if (readedRows == 0)
                        {
                            dataSet = new DataSet();
                        }

                        dataSet = this.formatter.Format(xml, dataSet) as DataSet;

                        readedRows++;

                        if (maxRowsToRead <= 0 || (maxRowsToRead > 0 && readedRows >= maxRowsToRead))
                        {
                            readedRows = 0;
                            yield return dataSet;
                        }
                    }
                }

                if (readedRows > 0)
                {
                    yield return dataSet;
                }
            }
        }

        public void WriteData(DataTable table, string fileName, string xPath)
        {
            var xmlDoc = new XmlDocument();
            var namespaceMgr = new XmlNamespaceManager(xmlDoc.NameTable);

            var isNewFile = this.IsNewFile(fileName);
            if (isNewFile)
            {
                // add Declaration
                XmlNode docNode = xmlDoc.CreateXmlDeclaration("1.0", "UTF-8", null);
                xmlDoc.AppendChild(docNode);

                // create base Path
                XPathUtil.CreateXPath(xmlDoc, xPath);
            }
            else
            {
                xmlDoc.Load(fileName);
            }

            var xmlLines = this.formatter.Format(table, null) as IEnumerable<string>;

            int writtenRows = 0;
            int rowIdx = 0;

            if (xmlLines != null)
            {
                foreach (var xmlLine in xmlLines)
                {
                    var lastNode = xmlDoc.SelectSingleNode(xPath + "[last()]", namespaceMgr);

                    if (lastNode != null)
                    {
                        // Append xml to the last node
                        var xmlDocFragment = xmlDoc.CreateDocumentFragment();
                        xmlDocFragment.InnerXml = xmlLine;
                        lastNode.AppendChild(xmlDocFragment);

                        writtenRows++;
                    }

                    rowIdx++;
                }
            }

            var settings = new XmlWriterSettings { Indent = true };
            using (XmlWriter writer = XmlWriter.Create(fileName, settings))
            {
                xmlDoc.Save(writer);
                writer.Close();
            }
        }

        private bool IsNewFile(string fileName)
        {
            // new File?
            if (!string.IsNullOrEmpty(fileName) && !File.Exists(fileName))
            {
                return true;
            }

            return false;
        }
    }
}