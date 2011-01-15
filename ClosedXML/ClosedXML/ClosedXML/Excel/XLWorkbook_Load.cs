﻿using DocumentFormat.OpenXml.Packaging;
using Ap = DocumentFormat.OpenXml.ExtendedProperties;
using Vt = DocumentFormat.OpenXml.VariantTypes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using C = DocumentFormat.OpenXml.Drawing.Charts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;



namespace ClosedXML.Excel
{
    public partial class XLWorkbook
    {
        private void Load(String file)
        {
            LoadSheets(file);
        }
        private void Load(Stream stream)
        {
            LoadSheets(stream);
        }
        private void LoadSheets(String fileName)
        {
            using (SpreadsheetDocument dSpreadsheet = SpreadsheetDocument.Open(fileName, false))
            {
                LoadSpreadsheetDocument(dSpreadsheet);
            }
        }
        private void LoadSheets(Stream stream)
        {
            using (SpreadsheetDocument dSpreadsheet = SpreadsheetDocument.Open(stream, false))
            {
                LoadSpreadsheetDocument(dSpreadsheet);
            }
        }
        private void LoadSpreadsheetDocument(SpreadsheetDocument dSpreadsheet)
        {
            SetProperties(dSpreadsheet);
            SharedStringItem[] sharedStrings = null;
            if (dSpreadsheet.WorkbookPart.GetPartsOfType<SharedStringTablePart>().Count() > 0)
            {
                SharedStringTablePart shareStringPart = dSpreadsheet.WorkbookPart.GetPartsOfType<SharedStringTablePart>().First();
                sharedStrings = shareStringPart.SharedStringTable.Elements<SharedStringItem>().ToArray();
            }

            var referenceMode = dSpreadsheet.WorkbookPart.Workbook.CalculationProperties.ReferenceMode;
            if (referenceMode != null)
            {
                ReferenceStyle = referenceModeValues.Single(p => p.Value == referenceMode.Value).Key;
            }

            var calculateMode = dSpreadsheet.WorkbookPart.Workbook.CalculationProperties.CalculationMode;
            if (calculateMode != null)
            {
                CalculateMode = calculateModeValues.Single(p => p.Value == calculateMode.Value).Key;
            }

            if (dSpreadsheet.ExtendedFilePropertiesPart.Properties.Elements<Ap.Company>().Count() > 0)
                Properties.Company = dSpreadsheet.ExtendedFilePropertiesPart.Properties.GetFirstChild<Ap.Company>().Text;

            if (dSpreadsheet.ExtendedFilePropertiesPart.Properties.Elements<Ap.Manager>().Count() > 0)
                Properties.Manager = dSpreadsheet.ExtendedFilePropertiesPart.Properties.GetFirstChild<Ap.Manager>().Text;


            var workbookStylesPart = (WorkbookStylesPart)dSpreadsheet.WorkbookPart.WorkbookStylesPart;
            var s = (Stylesheet)workbookStylesPart.Stylesheet;
            var numberingFormats = (NumberingFormats)s.NumberingFormats;
            Fills fills = (Fills)s.Fills;
            Borders borders = (Borders)s.Borders;
            Fonts fonts = (Fonts)s.Fonts;

            var sheets = dSpreadsheet.WorkbookPart.Workbook.Sheets;

            foreach (var sheet in sheets)
            {
                var sharedFormulas = new Dictionary<UInt32, CellFormula>();

                Sheet dSheet = ((Sheet)sheet);
                WorksheetPart worksheetPart = (WorksheetPart)dSpreadsheet.WorkbookPart.GetPartById(dSheet.Id);

                var sheetName = dSheet.Name;

                var ws = (XLWorksheet)Worksheets.Add(sheetName);
                ws.RelId = dSheet.Id;
                var sheetFormatProperties = (SheetFormatProperties)worksheetPart.Worksheet.Descendants<SheetFormatProperties>().First();
                if (sheetFormatProperties.DefaultRowHeight != null)
                    ws.RowHeight = sheetFormatProperties.DefaultRowHeight;

                if (sheetFormatProperties.DefaultColumnWidth != null)
                    ws.ColumnWidth = sheetFormatProperties.DefaultColumnWidth;

                var sheetView = (SheetView)worksheetPart.Worksheet.Descendants<SheetView>().FirstOrDefault();
                if (sheetView != null)
                {
                    var pane = (Pane)sheetView.Descendants<Pane>().FirstOrDefault();
                    if (pane != null)
                    {
                        if (pane.State != null && pane.State == PaneStateValues.FrozenSplit)
                        {
                            if (pane.HorizontalSplit != null)
                                ws.SheetView.SplitColumn = (Int32)pane.HorizontalSplit.Value;
                            if (pane.VerticalSplit != null)
                                ws.SheetView.SplitRow = (Int32)pane.VerticalSplit.Value;
                        }
                    }
                }

                foreach (var mCell in worksheetPart.Worksheet.Descendants<MergeCell>())
                {
                    var mergeCell = (MergeCell)mCell;
                    ws.Range(mergeCell.Reference).Merge();
                }

                Column wsDefaultColumn = null;
                var defaultColumns = worksheetPart.Worksheet.Descendants<Column>().Where(c => c.Max == XLWorksheet.MaxNumberOfColumns);
                if (defaultColumns.Count() > 0)
                    wsDefaultColumn = defaultColumns.Single();

                if (wsDefaultColumn != null && wsDefaultColumn.Width != null) ws.ColumnWidth = wsDefaultColumn.Width;

                Int32 styleIndexDefault = wsDefaultColumn != null && wsDefaultColumn.Style != null ? Int32.Parse(wsDefaultColumn.Style.InnerText) : -1;
                if (styleIndexDefault >= 0)
                {
                    ApplyStyle(ws, styleIndexDefault, s, fills, borders, fonts, numberingFormats);
                }

                foreach (var col in worksheetPart.Worksheet.Descendants<Column>())
                {
                    //IXLStylized toApply;
                    if (col.Max != XLWorksheet.MaxNumberOfColumns)
                    {
                        var xlColumns = (XLColumns)ws.Columns(col.Min, col.Max);
                        if (col.Width != null)
                            xlColumns.Width = col.Width;
                        else
                            xlColumns.Width = ws.ColumnWidth;

                        if (col.Hidden != null && col.Hidden)
                            xlColumns.Hide();

                        if (col.Collapsed != null && col.Collapsed)
                            xlColumns.Collapse();

                        if (col.OutlineLevel != null)
                            xlColumns.ForEach(c => c.OutlineLevel = col.OutlineLevel);

                        Int32 styleIndex = col.Style != null ? Int32.Parse(col.Style.InnerText) : -1;
                        if (styleIndex > 0)
                        {
                            ApplyStyle(xlColumns, styleIndex, s, fills, borders, fonts, numberingFormats);
                        }
                        else
                        {
                            xlColumns.Style = DefaultStyle;
                        }
                    }
                }

                foreach (var row in worksheetPart.Worksheet.Descendants<Row>()) //.Where(r => r.CustomFormat != null && r.CustomFormat).Select(r => r))
                {
                    var xlRow = (XLRow)ws.Row((Int32)row.RowIndex.Value, false);
                    if (row.Height != null)
                        xlRow.Height = row.Height;
                    else
                        xlRow.Height = ws.RowHeight;

                    if (row.Hidden != null && row.Hidden)
                        xlRow.Hide();

                    if (row.Collapsed != null && row.Collapsed)
                        xlRow.Collapse();

                    if (row.OutlineLevel != null)
                        xlRow.OutlineLevel = row.OutlineLevel;

                    if (row.CustomFormat != null)
                    {
                        Int32 styleIndex = row.StyleIndex != null ? Int32.Parse(row.StyleIndex.InnerText) : -1;
                        if (styleIndex > 0)
                        {
                            ApplyStyle(xlRow, styleIndex, s, fills, borders, fonts, numberingFormats);
                        }
                        else
                        {
                            //((XLRow)xlRow).style = ws.Style;
                            //((XLRow)xlRow).SetStyleNoColumns(ws.Style);
                            xlRow.Style = DefaultStyle;
                            //xlRow.Style = ws.Style;
                        }
                    }
                }

                foreach (var cell in worksheetPart.Worksheet.Descendants<Cell>())
                {
                    if (cell.CellFormula != null && cell.CellFormula.SharedIndex != null && cell.CellFormula.Reference != null)
                        sharedFormulas.Add(cell.CellFormula.SharedIndex.Value, cell.CellFormula);

                    var dCell = (Cell)cell;
                    Int32 styleIndex = dCell.StyleIndex != null ? Int32.Parse(dCell.StyleIndex.InnerText) : 0;
                    var xlCell = (XLCell)ws.CellFast(dCell.CellReference);

                    if (styleIndex > 0)
                    {
                        //styleIndex = Int32.Parse(dCell.StyleIndex.InnerText);
                        ApplyStyle(xlCell, styleIndex, s, fills, borders, fonts, numberingFormats);
                    }
                    else
                    {
                        xlCell.Style = DefaultStyle;
                    }

                    if (dCell.CellFormula != null)
                    {
                        if (dCell.CellFormula.SharedIndex != null)
                            xlCell.FormulaA1 = sharedFormulas[dCell.CellFormula.SharedIndex.Value].Text;
                        else
                            xlCell.FormulaA1 = dCell.CellFormula.Text;
                    }
                    else if (dCell.DataType != null)
                    {
                        if (dCell.DataType == CellValues.SharedString)
                        {
                            if (dCell.CellValue != null)
                            {
                                if (!StringExtensions.IsNullOrWhiteSpace(dCell.CellValue.Text))
                                    xlCell.cellValue = sharedStrings[Int32.Parse(dCell.CellValue.Text)].InnerText;
                                else
                                    xlCell.cellValue = dCell.CellValue.Text;
                            }
                            else
                            {
                                xlCell.cellValue = String.Empty;
                            }
                            xlCell.DataType = XLCellValues.Text;
                        }
                        else if (dCell.DataType == CellValues.Date)
                        {
                            xlCell.Value = DateTime.FromOADate(Double.Parse(dCell.CellValue.Text));
                        }
                        else if (dCell.DataType == CellValues.Boolean)
                        {
                            xlCell.Value = (dCell.CellValue.Text == "1");
                        }
                        else if (dCell.DataType == CellValues.Number)
                        {
                            xlCell.Value = dCell.CellValue.Text;
                            var numberFormatId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).NumberFormatId;
                            if (numberFormatId == 46U)
                                xlCell.DataType = XLCellValues.TimeSpan;
                            else
                                xlCell.DataType = XLCellValues.Number;
                        }
                    }
                    else if (dCell.CellValue != null)
                    {
                        //var styleIndex = Int32.Parse(dCell.StyleIndex.InnerText);
                        var numberFormatId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).NumberFormatId; //. [styleIndex].NumberFormatId;
                        ws.Cell(dCell.CellReference).Value = dCell.CellValue.Text;
                        ws.Cell(dCell.CellReference).Style.NumberFormat.NumberFormatId = Int32.Parse(numberFormatId);
                    }

                    
                }
                foreach (var tablePart in worksheetPart.TableDefinitionParts)
                {
                    var dTable = (Table)tablePart.Table;
                    var reference = dTable.Reference.Value;
                    var xlTable = ws.Range(reference).CreateTable(dTable.Name);
                    if (dTable.TotalsRowCount != null && dTable.TotalsRowCount.Value > 0)
                        ((XLTable)xlTable).showTotalsRow = true;

                    if (dTable.TableStyleInfo != null)
                    {
                        if (dTable.TableStyleInfo.ShowFirstColumn != null)
                            xlTable.EmphasizeFirstColumn = dTable.TableStyleInfo.ShowFirstColumn.Value;
                        if (dTable.TableStyleInfo.ShowLastColumn != null)
                            xlTable.EmphasizeLastColumn = dTable.TableStyleInfo.ShowLastColumn.Value;
                        if (dTable.TableStyleInfo.ShowRowStripes != null)
                            xlTable.ShowRowStripes = dTable.TableStyleInfo.ShowRowStripes.Value;
                        if (dTable.TableStyleInfo.ShowColumnStripes != null)
                            xlTable.ShowColumnStripes = dTable.TableStyleInfo.ShowColumnStripes.Value;
                        if (dTable.TableStyleInfo.Name != null)
                            xlTable.Theme = (XLTableTheme)Enum.Parse(typeof(XLTableTheme), dTable.TableStyleInfo.Name.Value);
                    }

                    xlTable.ShowAutoFilter = dTable.AutoFilter != null;

                    foreach (var column in dTable.TableColumns)
                    {
                        var tableColumn = (TableColumn)column;
                        if (tableColumn.TotalsRowFunction != null)
                            xlTable.Field(tableColumn.Name.Value).TotalsRowFunction = totalsRowFunctionValues.Single(p => p.Value == tableColumn.TotalsRowFunction.Value).Key;

                        if (tableColumn.TotalsRowFormula != null)
                            xlTable.Field(tableColumn.Name.Value).TotalsRowFormulaA1 = tableColumn.TotalsRowFormula.Text;

                        if (tableColumn.TotalsRowLabel != null)
                            xlTable.Field(tableColumn.Name.Value).TotalsRowLabel = tableColumn.TotalsRowLabel.Value;
                    }
                }
                var printOptionsQuery = worksheetPart.Worksheet.Descendants<PrintOptions>();
                if (printOptionsQuery.Count() > 0)
                {
                    var printOptions = (PrintOptions)printOptionsQuery.First();
                    if (printOptions.GridLines != null)
                        ws.PageSetup.ShowGridlines = printOptions.GridLines;
                    if (printOptions.HorizontalCentered != null)
                        ws.PageSetup.CenterHorizontally = printOptions.HorizontalCentered;
                    if (printOptions.VerticalCentered != null)
                        ws.PageSetup.CenterVertically = printOptions.VerticalCentered;
                    if (printOptions.Headings != null)
                        ws.PageSetup.ShowRowAndColumnHeadings = printOptions.Headings;
                }

                var pageMarginsQuery = worksheetPart.Worksheet.Descendants<PageMargins>();
                if (pageMarginsQuery.Count() > 0)
                {
                    var pageMargins = (PageMargins)pageMarginsQuery.First();
                    if (pageMargins.Bottom != null)
                        ws.PageSetup.Margins.Bottom = pageMargins.Bottom;
                    if (pageMargins.Footer != null)
                        ws.PageSetup.Margins.Footer = pageMargins.Footer;
                    if (pageMargins.Header != null)
                        ws.PageSetup.Margins.Header = pageMargins.Header;
                    if (pageMargins.Left != null)
                        ws.PageSetup.Margins.Left = pageMargins.Left;
                    if (pageMargins.Right != null)
                        ws.PageSetup.Margins.Right = pageMargins.Right;
                    if (pageMargins.Top != null)
                        ws.PageSetup.Margins.Top = pageMargins.Top;
                }

                var pageSetupQuery = worksheetPart.Worksheet.Descendants<PageSetup>();
                if (pageSetupQuery.Count() > 0)
                {
                    var pageSetup = (PageSetup)pageSetupQuery.First();
                    if (pageSetup.PaperSize != null)
                        ws.PageSetup.PaperSize = (XLPaperSize)Int32.Parse(pageSetup.PaperSize.InnerText);
                    if (pageSetup.Scale != null)
                    {
                        ws.PageSetup.Scale = Int32.Parse(pageSetup.Scale.InnerText);
                    }
                    else
                    {
                        if (pageSetup.FitToWidth != null)
                            ws.PageSetup.PagesWide = Int32.Parse(pageSetup.FitToWidth.InnerText);
                        if (pageSetup.FitToHeight != null)
                            ws.PageSetup.PagesTall = Int32.Parse(pageSetup.FitToHeight.InnerText);
                    }
                    if (pageSetup.PageOrder != null)
                        ws.PageSetup.PageOrder = pageOrderValues.Single(p => p.Value == pageSetup.PageOrder).Key;
                    if (pageSetup.Orientation != null)
                        ws.PageSetup.PageOrientation = pageOrientationValues.Single(p => p.Value == pageSetup.Orientation).Key;
                    if (pageSetup.BlackAndWhite != null)
                        ws.PageSetup.BlackAndWhite = pageSetup.BlackAndWhite;
                    if (pageSetup.Draft != null)
                        ws.PageSetup.DraftQuality = pageSetup.Draft;
                    if (pageSetup.CellComments != null)
                        ws.PageSetup.ShowComments = showCommentsValues.Single(sc => sc.Value == pageSetup.CellComments).Key;
                    if (pageSetup.Errors != null)
                        ws.PageSetup.PrintErrorValue = printErrorValues.Single(p => p.Value == pageSetup.Errors).Key;
                    if (pageSetup.HorizontalDpi != null) ws.PageSetup.HorizontalDpi = Int32.Parse(pageSetup.HorizontalDpi.InnerText);
                    if (pageSetup.VerticalDpi != null) ws.PageSetup.VerticalDpi = Int32.Parse(pageSetup.VerticalDpi.InnerText);
                    if (pageSetup.FirstPageNumber != null) ws.PageSetup.FirstPageNumber = Int32.Parse(pageSetup.FirstPageNumber.InnerText);
                }

                var headerFooters = worksheetPart.Worksheet.Descendants<HeaderFooter>();
                if (headerFooters.Count() > 0)
                {
                    var headerFooter = (HeaderFooter)headerFooters.First();
                    if (headerFooter.AlignWithMargins != null)
                        ws.PageSetup.AlignHFWithMargins = headerFooter.AlignWithMargins;
                    if (headerFooter.ScaleWithDoc != null)
                        ws.PageSetup.ScaleHFWithDocument = headerFooter.ScaleWithDoc;

                    // Footers
                    var xlFooter = (XLHeaderFooter)ws.PageSetup.Footer;
                    var evenFooter = (EvenFooter)headerFooter.EvenFooter;
                    if (evenFooter != null)
                        xlFooter.SetInnerText(XLHFOccurrence.EvenPages, evenFooter.Text);
                    var oddFooter = (OddFooter)headerFooter.OddFooter;
                    if (oddFooter != null)
                        xlFooter.SetInnerText(XLHFOccurrence.OddPages, oddFooter.Text);
                    var firstFooter = (FirstFooter)headerFooter.FirstFooter;
                    if (firstFooter != null)
                        xlFooter.SetInnerText(XLHFOccurrence.FirstPage, firstFooter.Text);
                    // Headers
                    var xlHeader = (XLHeaderFooter)ws.PageSetup.Header;
                    var evenHeader = (EvenHeader)headerFooter.EvenHeader;
                    if (evenHeader != null)
                        xlHeader.SetInnerText(XLHFOccurrence.EvenPages, evenHeader.Text);
                    var oddHeader = (OddHeader)headerFooter.OddHeader;
                    if (oddHeader != null)
                        xlHeader.SetInnerText(XLHFOccurrence.OddPages, oddHeader.Text);
                    var firstHeader = (FirstHeader)headerFooter.FirstHeader;
                    if (firstHeader != null)
                        xlHeader.SetInnerText(XLHFOccurrence.FirstPage, firstHeader.Text);
                }

                var sheetProperties = worksheetPart.Worksheet.Descendants<SheetProperties>();
                if (sheetProperties.Count() > 0)
                {
                    var sheetProperty = (SheetProperties)sheetProperties.First();
                    if (sheetProperty.OutlineProperties != null)
                    {
                        if (sheetProperty.OutlineProperties.SummaryBelow != null)
                        {
                            ws.Outline.SummaryVLocation = sheetProperty.OutlineProperties.SummaryBelow ?
                                XLOutlineSummaryVLocation.Bottom : XLOutlineSummaryVLocation.Top;
                        }

                        if (sheetProperty.OutlineProperties.SummaryRight != null)
                        {
                            ws.Outline.SummaryHLocation = sheetProperty.OutlineProperties.SummaryRight ?
                                XLOutlineSummaryHLocation.Right : XLOutlineSummaryHLocation.Left;
                        }
                    }
                }

                var rowBreaksList = worksheetPart.Worksheet.Descendants<RowBreaks>();
                if (rowBreaksList.Count() > 0)
                {
                    var rowBreaks = (RowBreaks)rowBreaksList.First();
                    foreach (var rowBreak in rowBreaks.Descendants<Break>())
                    {
                        ws.PageSetup.RowBreaks.Add(Int32.Parse(rowBreak.Id.InnerText));
                    }
                }

                var columnBreaksList = worksheetPart.Worksheet.Descendants<ColumnBreaks>();
                if (columnBreaksList.Count() > 0)
                {
                    var columnBreaks = (ColumnBreaks)columnBreaksList.First();
                    foreach (var columnBreak in columnBreaks.Descendants<Break>())
                    {
                        if (columnBreak.Id != null)
                            ws.PageSetup.ColumnBreaks.Add(Int32.Parse(columnBreak.Id.InnerText));
                    }
                }
            }

            var workbook = (Workbook)dSpreadsheet.WorkbookPart.Workbook;
            foreach (var definedName in workbook.Descendants<DefinedName>())
            {
                var name = definedName.Name;
                if (name == "_xlnm.Print_Area")
                {
                    foreach (var area in definedName.Text.Split(','))
                    {
                        var sections = area.Trim().Split('!');
                        var sheetName = sections[0].Replace("\'", "");
                        var sheetArea = sections[1];
                        Worksheets.Worksheet(sheetName).PageSetup.PrintAreas.Add(sheetArea);
                    }
                }
                else if (name == "_xlnm.Print_Titles")
                {
                    var areas = definedName.Text.Split(',');

                    var colSections = areas[0].Trim().Split('!');
                    var sheetNameCol = colSections[0].Replace("\'", "");
                    var sheetAreaCol = colSections[1];
                    Worksheets.Worksheet(sheetNameCol).PageSetup.SetColumnsToRepeatAtLeft(sheetAreaCol);

                    var rowSections = areas[1].Split('!');
                    var sheetNameRow = rowSections[0].Replace("\'", "");
                    var sheetAreaRow = rowSections[1];
                    Worksheets.Worksheet(sheetNameRow).PageSetup.SetRowsToRepeatAtTop(sheetAreaRow);
                }
                else
                {
                    var localSheetId = definedName.LocalSheetId;
                    var comment = definedName.Comment;
                    var text = definedName.Text;
                    if (localSheetId == null)
                    {
                        NamedRanges.Add(name, text, comment);
                    }
                    else
                    {
                        Worksheets.Worksheet(Int32.Parse(localSheetId)).NamedRanges.Add(name, text, comment);
                    }
                }
            }


        }

        private void SetProperties(SpreadsheetDocument dSpreadsheet)
        {
            var p = dSpreadsheet.PackageProperties;
            Properties.Author = p.Creator;
            Properties.Category = p.Category;
            Properties.Comments = p.Description;
            if (p.Created != null)
                Properties.Created = p.Created.Value;
            Properties.Keywords = p.Keywords;
            Properties.LastModifiedBy = p.LastModifiedBy;
            Properties.Status = p.ContentStatus;
            Properties.Subject = p.Subject;
            Properties.Title = p.Title;
        }

        private Dictionary<String, System.Drawing.Color> colorList = new Dictionary<string, System.Drawing.Color>();
        private IXLColor GetColor(ColorType color)
        {
            IXLColor retVal = null;
            if (color != null)
            {
                if (color.Rgb != null)
                {
                    String htmlColor = "#" + color.Rgb.Value;
                    System.Drawing.Color thisColor;    
                    if (!colorList.ContainsKey(htmlColor))
                    {
                        thisColor = System.Drawing.ColorTranslator.FromHtml(htmlColor);
                        colorList.Add(htmlColor, thisColor);
                    }
                    else
                    {
                        thisColor = colorList[htmlColor];
                    }
                    retVal = new XLColor(thisColor);
                }
                else if (color.Indexed != null && color.Indexed < 64)
                {
                    retVal = new XLColor((Int32)color.Indexed.Value);
                }
                else if (color.Theme != null)
                {
                    if (color.Tint != null)
                        retVal = XLColor.FromTheme((XLThemeColor)color.Theme.Value, color.Tint.Value);
                    else
                        retVal = XLColor.FromTheme((XLThemeColor)color.Theme.Value);
                }
            }
            if (retVal == null)
                return new XLColor();
            else
                return retVal;
        }

        private void ApplyStyle(IXLStylized xlStylized, Int32 styleIndex, Stylesheet s, Fills fills, Borders borders, Fonts fonts, NumberingFormats numberingFormats)
        {
            //if (fills.ContainsKey(styleIndex))
            //{
            //    var fill = fills[styleIndex];
            var fillId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).FillId.Value;
            if (fillId > 0)
            {
                var fill = (Fill)fills.ElementAt((Int32)fillId);
                if (fill.PatternFill != null)
                {
                    if (fill.PatternFill.PatternType != null)
                        xlStylized.InnerStyle.Fill.PatternType = fillPatternValues.Single(p => p.Value == fill.PatternFill.PatternType).Key;

                    var fgColor = GetColor(fill.PatternFill.ForegroundColor);
                    if (fgColor.HasValue) xlStylized.InnerStyle.Fill.PatternColor = fgColor;

                    var bgColor = GetColor(fill.PatternFill.BackgroundColor);
                    if (bgColor.HasValue) 
                        xlStylized.InnerStyle.Fill.PatternBackgroundColor = bgColor;
                }
            }

            //var alignmentDictionary = GetAlignmentDictionary(s);

            //if (alignmentDictionary.ContainsKey(styleIndex))
            //{
            //    var alignment = alignmentDictionary[styleIndex];
            var alignment = (Alignment)((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).Alignment;
            if (alignment != null)
            {
                if (alignment.Horizontal != null)
                    xlStylized.InnerStyle.Alignment.Horizontal = alignmentHorizontalValues.Single(a => a.Value == alignment.Horizontal).Key;
                if (alignment.Indent != null)
                    xlStylized.InnerStyle.Alignment.Indent = (Int32)alignment.Indent.Value;
                if (alignment.JustifyLastLine != null)
                    xlStylized.InnerStyle.Alignment.JustifyLastLine = alignment.JustifyLastLine;
                if (alignment.ReadingOrder != null)
                    xlStylized.InnerStyle.Alignment.ReadingOrder = (XLAlignmentReadingOrderValues)(Int32)alignment.ReadingOrder.Value ;
                if (alignment.RelativeIndent != null)
                    xlStylized.InnerStyle.Alignment.RelativeIndent = alignment.RelativeIndent;
                if (alignment.ShrinkToFit != null)
                    xlStylized.InnerStyle.Alignment.ShrinkToFit = alignment.ShrinkToFit;
                if (alignment.TextRotation != null)
                    xlStylized.InnerStyle.Alignment.TextRotation = (Int32)alignment.TextRotation.Value;
                if (alignment.Vertical != null)
                    xlStylized.InnerStyle.Alignment.Vertical = alignmentVerticalValues.Single(a => a.Value == alignment.Vertical).Key;
                if (alignment.WrapText !=null)
                    xlStylized.InnerStyle.Alignment.WrapText = alignment.WrapText;
            }


            //if (borders.ContainsKey(styleIndex))
            //{
            //    var border = borders[styleIndex];
            var borderId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).BorderId.Value;
            var border = (Border)borders.ElementAt((Int32)borderId);
            if (border != null)
            {
                var bottomBorder = (BottomBorder)border.BottomBorder;
                if (bottomBorder != null)
                {
                    if (bottomBorder.Style != null)
                        xlStylized.InnerStyle.Border.BottomBorder = borderStyleValues.Single(b => b.Value == bottomBorder.Style.Value).Key;

                    var bottomBorderColor = GetColor(bottomBorder.Color);
                    if (bottomBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.BottomBorderColor = bottomBorderColor;
                }
                var topBorder = (TopBorder)border.TopBorder;
                if (topBorder != null)
                {
                    if (topBorder.Style != null)
                        xlStylized.InnerStyle.Border.TopBorder = borderStyleValues.Single(b => b.Value == topBorder.Style.Value).Key;
                    var topBorderColor = GetColor(topBorder.Color);
                    if (topBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.TopBorderColor = topBorderColor;
                }
                var leftBorder = (LeftBorder)border.LeftBorder;
                if (leftBorder != null)
                {
                    if (leftBorder.Style != null)
                        xlStylized.InnerStyle.Border.LeftBorder = borderStyleValues.Single(b => b.Value == leftBorder.Style.Value).Key;
                    var leftBorderColor = GetColor(leftBorder.Color);
                    if (leftBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.LeftBorderColor = leftBorderColor;
                }
                var rightBorder = (RightBorder)border.RightBorder;
                if (rightBorder != null)
                {
                    if (rightBorder.Style != null)
                        xlStylized.InnerStyle.Border.RightBorder = borderStyleValues.Single(b => b.Value == rightBorder.Style.Value).Key;
                    var rightBorderColor = GetColor(rightBorder.Color);
                    if (rightBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.RightBorderColor = rightBorderColor;
                }
                var diagonalBorder = (DiagonalBorder)border.DiagonalBorder;
                if (diagonalBorder != null)
                {
                    if (diagonalBorder.Style != null)
                        xlStylized.InnerStyle.Border.DiagonalBorder = borderStyleValues.Single(b => b.Value == diagonalBorder.Style.Value).Key;
                    var diagonalBorderColor = GetColor(diagonalBorder.Color);
                    if (diagonalBorderColor.HasValue)
                        xlStylized.InnerStyle.Border.DiagonalBorderColor = diagonalBorderColor;
                    if (border.DiagonalDown != null)
                        xlStylized.InnerStyle.Border.DiagonalDown = border.DiagonalDown;
                    if (border.DiagonalUp != null)
                        xlStylized.InnerStyle.Border.DiagonalUp = border.DiagonalUp;
                }
            }

            //if (fonts.ContainsKey(styleIndex))
            //{
            //    var font = fonts[styleIndex];
            var fontId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).FontId;
            var font = (Font)fonts.ElementAt((Int32)fontId.Value);
            if (font != null)
            {
                xlStylized.InnerStyle.Font.Bold = GetBoolean(font.Bold);

                var fontColor = GetColor(font.Color);
                if (fontColor.HasValue)
                    xlStylized.InnerStyle.Font.FontColor = fontColor;

                if (font.FontFamilyNumbering != null)
                    if (font.FontFamilyNumbering.Val.HasValue)
                        xlStylized.InnerStyle.Font.FontFamilyNumbering = (XLFontFamilyNumberingValues)font.FontFamilyNumbering.Val.Value;
                if (font.FontName != null)
                {
                    if (font.FontName.Val != null)
                        xlStylized.InnerStyle.Font.FontName = font.FontName.Val;
                }
                if (font.FontSize != null)
                {
                    if (font.FontSize.Val != null)
                        xlStylized.InnerStyle.Font.FontSize = font.FontSize.Val;
                }

                xlStylized.InnerStyle.Font.Italic = GetBoolean(font.Italic);
                xlStylized.InnerStyle.Font.Shadow = GetBoolean(font.Shadow);
                xlStylized.InnerStyle.Font.Strikethrough = GetBoolean(font.Strike);
                
                if (font.Underline != null)
                    if (font.Underline.Val != null)
                        xlStylized.InnerStyle.Font.Underline = underlineValuesList.Single(u => u.Value == ((Underline)font.Underline).Val).Key;
                    else
                        xlStylized.InnerStyle.Font.Underline = XLFontUnderlineValues.Single;

                if (font.VerticalTextAlignment != null)
                    
                if (font.VerticalTextAlignment.Val != null)
                    xlStylized.InnerStyle.Font.VerticalAlignment = fontVerticalTextAlignmentValues.Single(f => f.Value == ((VerticalTextAlignment)font.VerticalTextAlignment).Val).Key;
                else
                    xlStylized.InnerStyle.Font.VerticalAlignment = XLFontVerticalTextAlignmentValues.Baseline;
            }
            if (s.CellFormats != null)
            {
                var numberFormatId = ((CellFormat)((CellFormats)s.CellFormats).ElementAt(styleIndex)).NumberFormatId;
                if (numberFormatId != null)
                {
                    var formatCode = String.Empty;
                    if (numberingFormats != null)
                    {
                        var numberFormatList = numberingFormats.Where(nf => ((NumberingFormat)nf).NumberFormatId != null && ((NumberingFormat)nf).NumberFormatId.Value == numberFormatId);

                        if (numberFormatList.Count() > 0)
                        {
                            NumberingFormat numberingFormat = (NumberingFormat)numberFormatList.First();
                            if (numberingFormat.FormatCode != null)
                                formatCode = numberingFormat.FormatCode.Value;
                        }
                    }
                    if (formatCode.Length > 0)
                        xlStylized.InnerStyle.NumberFormat.Format = formatCode;
                    else
                        xlStylized.InnerStyle.NumberFormat.NumberFormatId = (Int32)numberFormatId.Value;
                }
            }
        }

        private Boolean GetBoolean(BooleanPropertyType property)
        {
            if (property != null)
            {
                if (property.Val != null)
                    return property.Val;
                else
                    return true;
            }
            else
            {
                return false;
            }
        }




    }
}