using System;
using System.Diagnostics;
using System.IO;
using System.Collections;
using OTFontFile.Rasterizer;

using OTFontFile;

namespace OTFontFileVal
{
    public class OTFontVal : OTFont
    {
        /***************
         * constructors
         */
        
        
        public OTFontVal(OTFile f) : base (f)
        {
        }

        
        public OTFontVal(OTFile f, uint FontFileNumber, OffsetTable ot, OutlineType outlineType) : base (f, FontFileNumber, ot, outlineType)
        {
        }

        
        /************************
         * public static methods
         */
        
        
        public static new OTFontVal ReadFont(OTFile file, uint FontFileNumber, uint filepos)
        {
            OTFontVal f = null;

            OffsetTable ot = ReadOffsetTable(file, filepos);

            if (ot != null)
            {
                if (ot.numTables == ot.DirectoryEntries.Count)
                {
                    OutlineType olt = OutlineType.OUTLINE_INVALID;
                    for (int i = 0; i<ot.DirectoryEntries.Count; i++)
                    {
                        DirectoryEntry temp = (DirectoryEntry)ot.DirectoryEntries[i];
                        string sTable = (string)temp.tag;
                        if (sTable == "CFF ")
                        {
                            olt = OutlineType.OUTLINE_POSTSCRIPT;
                            break;
                        }
                        else if (sTable == "glyf")
                        {
                            olt = OutlineType.OUTLINE_TRUETYPE;
                            break;
                        }
                    }

                    f = new OTFontVal(file, FontFileNumber, ot, olt);
                }
            }
            return f;
        }

        
        /*****************
         * public methods
         */
        
        
        public bool Validate()
        {
            bool bRet = true;
            int canrast;

            Validator v = GetFile().GetValidator();

            if (v.PerformTest(T._OFFSET_sfntVersion))
            {
                uint sfnt = m_OffsetTable.sfntVersion.GetUint();
                if (!OTFile.IsValidSfntVersion(sfnt))
                {
                    v.Error(T._OFFSET_sfntVersion, E._OFFSET_E_InvalidSFNT, null, "0x"+sfnt.ToString("x8"));
                    bRet = false;
                }
            }

            if (v.PerformTest(T._OFFSET_numTables))
            {
                if (m_OffsetTable.numTables == 0)
                {
                    v.Error(T._OFFSET_numTables, E._OFFSET_E_numTables, null);
                    bRet = false;
                }
                else
                {
                    v.Pass(T._OFFSET_numTables, P._OFFSET_P_numTables, null, m_OffsetTable.numTables.ToString());
                }
            }

            if (v.PerformTest(T._OFFSET_BinarySearchFields))
            {
                ushort numTables = m_OffsetTable.numTables;
                ushort CalculatedSearchRange   = (ushort)(util.MaxPower2LE(numTables) * 16);
                ushort CalculatedEntrySelector = util.Log2(util.MaxPower2LE(numTables));
                ushort CalculatedRangeShift    = (ushort)(numTables*16 - CalculatedSearchRange);

                bool bBinaryFieldsOk = true;

                if (m_OffsetTable.searchRange != CalculatedSearchRange)
                {
                    v.Error(T._OFFSET_BinarySearchFields, E._OFFSET_E_searchRange, null, m_OffsetTable.searchRange.ToString());
                    bBinaryFieldsOk = false;
                    bRet = false;
                }
                if (m_OffsetTable.entrySelector != CalculatedEntrySelector)
                {
                    v.Error(T._OFFSET_BinarySearchFields, E._OFFSET_E_entrySelector, null, m_OffsetTable.entrySelector.ToString());
                    bBinaryFieldsOk = false;
                    bRet = false;
                }
                if (m_OffsetTable.rangeShift != CalculatedRangeShift)
                {
                    v.Error(T._OFFSET_BinarySearchFields, E._OFFSET_E_rangeShift, null, m_OffsetTable.rangeShift.ToString());
                    bBinaryFieldsOk = false;
                    bRet = false;
                }

                if (bBinaryFieldsOk)
                {
                    v.Pass(T._OFFSET_BinarySearchFields, P._OFFSET_P_BinarySearchTables, null);
                }
            }

            bRet &= CheckDirectoryEntriesNonZero(v);

            bRet &= CheckTablesInFileAndNotOverlapping(v);

            bRet &= CheckNoDuplicateTags(v);

            if (v.PerformTest(T._DE_TagsAscendingOrder))
            {
                bRet &= CheckTagsAscending(v);
            }

            if (v.PerformTest(T._DE_TagNames))
            {
                bRet &= CheckTagNames(v);
            }

            if (v.PerformTest(T._DE_TableAlignment))
            {
                bRet &= CheckTableAlignment(v);
            }

            if (v.PerformTest(T._FONT_RequiredTables))
            {
                bRet &= CheckForRequiredTables(v);
            }

            if (v.PerformTest(T._FONT_RecommendedTables))
            {
                bRet &= CheckForRecommendedTables(v);
            }

            if (v.PerformTest(T._FONT_UnnecessaryTables))
            {
                bRet &= CheckForNoUnnecessaryTables(v);
            }

            if (v.PerformTest(T._FONT_OptimalTableOrder))
            {
                bRet &= CheckForOptimalTableOrder(v);
            }


            // Validate each table

            if (m_OffsetTable != null)
            {
                for (int i = 0; i<m_OffsetTable.DirectoryEntries.Count; i++)
                {
                    // check to see if user canceled validation
                    if (v.CancelFlag)
                    {
                        break;
                    }

                    // get the table
                    DirectoryEntry de = (DirectoryEntry)m_OffsetTable.DirectoryEntries[i];
                    OTTable table = GetFile().GetTableManager().GetTable(this, de);

                    // Will it really happen?
                    if (GetFile().GetTableManager().GetUnaliasedTableName(de.tag) == "DSIG" && GetFile().IsCollection()) continue;

                    // Call the function that validates a single table
                    bRet &= this.GetFile().ValidateTable(table, v, de, this);

                }
            }

            canrast = TestFontRasterization();
            ushort numGlyphs = GetMaxpNumGlyphs();

            // rasterization test - BW

            v.OnRastTestValidationEvent_BW(true);
            if (v.PeformRastTest_BW())
            {
                if (canrast > 0)
                {
                    try
                    {
                        // fetch the rasterizer object and initialize it with this font
                        RasterInterf ri = GetFile().GetRasterizer();
                        ri.RasterNewSfnt(GetFile().GetFileStream(), GetFontIndexInFile());

                        
                        // call the rasterizer
                        RasterInterf.UpdateProgressDelegate upg = new RasterInterf.UpdateProgressDelegate(v.OnTableProgress);
                        RasterInterf.RastTestErrorDelegate rted = new RasterInterf.RastTestErrorDelegate(v.OnRastTestError);
                        int x = v.GetRastTestXRes();
                        int y = v.GetRastTestYRes();
                        int [] pointsizes = v.GetRastTestPointSizes();
                        RastTestTransform rtt = v.GetRastTestTransform();
                        bRet &= ri.RastTest(
                            x, y, pointsizes,
                            rtt.stretchX, rtt.stretchY, rtt.rotation, rtt.skew, rtt.matrix, 
                            true, false, false, 0,
                            rted, upg, numGlyphs);
                        if (ri.GetRastErrorCount() == 0)
                        {
                            v.Pass(T.T_NULL, P._rast_P_rasterization, null);
                        }
                    }
                    catch (Exception e)
                    {
                        v.ApplicationError(T.T_NULL, E._rast_A_ExceptionUnhandled, null, e.Message + e.StackTrace);
                    }
                }
                else if (canrast == 0)
                {
                    v.Info(T.T_NULL, I._rast_I_rasterization, null, GetDevMetricsDataError());
                }
                else
                {
                    v.Error(T.T_NULL, E._rast_E_rasterization, null, GetDevMetricsDataError());
                    bRet = false;
                }
            }
            else
            {
                v.Info(I._TEST_I_RastTestNotSelected, null);
            }
            v.OnRastTestValidationEvent_BW(false);


            // rasterization test - Grayscale

            v.OnRastTestValidationEvent_Grayscale(true);
            if (v.PeformRastTest_Grayscale())
            {
                if (canrast > 0)
                {
                    try
                    {
                        // fetch the rasterizer object and initialize it with this font
                        RasterInterf ri = GetFile().GetRasterizer();
                        ri.RasterNewSfnt(GetFile().GetFileStream(), GetFontIndexInFile());

                        
                        // call the rasterizer
                        RasterInterf.UpdateProgressDelegate upg = new RasterInterf.UpdateProgressDelegate(v.OnTableProgress);
                        RasterInterf.RastTestErrorDelegate rted = new RasterInterf.RastTestErrorDelegate(v.OnRastTestError);
                        int x = v.GetRastTestXRes();
                        int y = v.GetRastTestYRes();
                        int [] pointsizes = v.GetRastTestPointSizes();
                        RastTestTransform rtt = v.GetRastTestTransform();
                        bRet &= ri.RastTest(
                            x, y, pointsizes,
                            rtt.stretchX, rtt.stretchY, rtt.rotation, rtt.skew, rtt.matrix, 
                            false, true, false, 0,
                            rted, upg, numGlyphs);
                        if (ri.GetRastErrorCount() == 0)
                        {
                            v.Pass(T.T_NULL, P._rast_P_rasterization, null);
                        }
                    }
                    catch (Exception e)
                    {
                        v.ApplicationError(T.T_NULL, E._rast_A_ExceptionUnhandled, null, e.Message + e.StackTrace);
                    }
                }
                else if (canrast == 0)
                {
                    v.Info(T.T_NULL, I._rast_I_rasterization, null, GetDevMetricsDataError());
                }
                else
                {
                    v.Error(T.T_NULL, E._rast_E_rasterization, null, GetDevMetricsDataError());
                    bRet = false;
                }
            }
            else
            {
                v.Info(I._TEST_I_RastTestNotSelected, null);
            }
            v.OnRastTestValidationEvent_Grayscale(false);



            // rasterization test - Cleartype

            v.OnRastTestValidationEvent_Cleartype(true);
            if (v.PeformRastTest_Cleartype())
            {
                if (canrast > 0)
                {
                    try
                    {
                        uint CTFlags = v.GetCleartypeFlags();

                        // fetch the rasterizer object and initialize it with this font
                        RasterInterf ri = GetFile().GetRasterizer();
                        ri.RasterNewSfnt(GetFile().GetFileStream(), GetFontIndexInFile());

                        
                        // call the rasterizer
                        RasterInterf.UpdateProgressDelegate upg = new RasterInterf.UpdateProgressDelegate(v.OnTableProgress);
                        RasterInterf.RastTestErrorDelegate rted = new RasterInterf.RastTestErrorDelegate(v.OnRastTestError);
                        int x = v.GetRastTestXRes();
                        int y = v.GetRastTestYRes();
                        int [] pointsizes = v.GetRastTestPointSizes();
                        RastTestTransform rtt = v.GetRastTestTransform();
                        bRet &= ri.RastTest(
                            x, y, pointsizes,
                            rtt.stretchX, rtt.stretchY, rtt.rotation, rtt.skew, rtt.matrix, 
                            false, false, true, CTFlags,
                            rted, upg, numGlyphs);
                        if (ri.GetRastErrorCount() == 0)
                        {
                            v.Pass(T.T_NULL, P._rast_P_rasterization, null);
                        }
                    }
                    catch (Exception e)
                    {
                        v.ApplicationError(T.T_NULL, E._rast_A_ExceptionUnhandled, null, e.Message + e.StackTrace);
                    }
                }
                else if (canrast == 0)
                {
                    v.Info(T.T_NULL, I._rast_I_rasterization, null, GetDevMetricsDataError());
                }
                else
                {
                    v.Error(T.T_NULL, E._rast_E_rasterization, null, GetDevMetricsDataError());
                    bRet = false;
                }
            }
            else
            {
                v.Info(I._TEST_I_RastTestNotSelected, null);
            }
            v.OnRastTestValidationEvent_Cleartype(false);

            return bRet;
        }

        public new OTTable GetTable(OTTag tag)
        {
            OTTable table = null;

            // find the directory entry in this font that matches the tag
            DirectoryEntry de = GetDirectoryEntry(tag);

            if (de != null)
            {
                // get the table from the table manager
                table = GetFile().GetTableManager().GetTable(this, de);
            }

            return table;
        }

        public new OTFileVal GetFile()
        {
            return (OTFileVal)m_File;
        }

        public RasterInterf.DevMetricsData GetCalculatedDevMetrics()
        {
            if (m_DevMetricsData != null)
            {
                return m_DevMetricsData;
            }

            // get numGlyphs

            ushort numGlyphs = GetMaxpNumGlyphs();


            // hdmx

            byte [] hdmxPointSizes = null;
            ushort maxHdmxPointSize = 0;

            Table_hdmx hdmxTable = (Table_hdmx)GetTable("hdmx");
            int bCalc_hdmx = 0;
            if (hdmxTable != null && GetFile().GetValidator().TestTable("hdmx"))
            {
                bCalc_hdmx = 1;

                // the hdmxPointSizes array stores a 1 for each pixel size represented in the hdmx table
                //hdmxPointSizes = new byte[numGlyphs];
                hdmxPointSizes = new byte[256];

                for (uint i=0; i<hdmxTable.NumberDeviceRecords; i++)
                {
                    Table_hdmx.DeviceRecord dr = hdmxTable.GetDeviceRecord(i, numGlyphs);
                    hdmxPointSizes[dr.PixelSize] = 1;
                    if (maxHdmxPointSize < dr.PixelSize)
                    {
                        maxHdmxPointSize = dr.PixelSize;
                    }
                }
            }

            
            // LTSH

            Table_LTSH LTSHTable = (Table_LTSH)GetTable("LTSH");
            int bCalc_LTSH = 0;
            if (LTSHTable != null && GetFile().GetValidator().TestTable("LTSH"))
            {
                bCalc_LTSH = 1;
            }


            // VDMX

            byte uchPixelHeightRangeStart = 8;//#define DEFAULT_PixelHeightRangeStart 8
            byte uchPixelHeightRangeEnd = 255;//#define DEFAULT_PixelHeightRangeEnd    255

            ushort [] VDMXxResolution = new ushort[1];
            ushort [] VDMXyResolution = new ushort[1];
            VDMXxResolution[0] = 72; // always calculate at least a 1:1 ratio (used for calculating hdmx and LTSH)
            VDMXyResolution[0] = 72; // cacheTT code used 72:72, I think using 1:1 here will make the cacheTT code fail
            ushort cVDMXResolutions = 1;

            Table_VDMX VDMXTable = (Table_VDMX)GetTable("VDMX");
            int bCalc_VDMX = 0;
            if (VDMXTable != null && GetFile().GetValidator().TestTable("VDMX"))
            {
                bCalc_VDMX = 1;

                Table_VDMX.Vdmx VdmxGroup = VDMXTable.GetVdmxGroup(0);

                uchPixelHeightRangeStart = VdmxGroup.startsz;
                uchPixelHeightRangeEnd   = VdmxGroup.endsz;

                VDMXxResolution = new ushort[VDMXTable.numRatios];
                VDMXyResolution = new ushort[VDMXTable.numRatios];

                for (uint i=0; i<VDMXTable.numRatios; i++)
                {
                    Table_VDMX.Ratios ratio = VDMXTable.GetRatioRange(i);
                    VDMXxResolution[i] = ratio.xRatio;
                    VDMXyResolution[i] = ratio.yStartRatio;

                    // cacheTT code seems to expect unreduced ratios
                    if (VDMXxResolution[i] < 72 && VDMXyResolution[i] < 72)
                    {
                        VDMXxResolution[i] *= 72;
                        VDMXyResolution[i] *= 72;
                    }
                }

                cVDMXResolutions = VDMXTable.numRatios;
            }


            // make sure that we are going to have a known problem with the
            // rasterizer due to missing tables or bad offsets
            if (TestFontRasterization() > 0)
            {
                // fetch the rasterizer object and initialize it with this font
                RasterInterf ri = GetFile().GetRasterizer();
                ri.RasterNewSfnt(GetFile().GetFileStream(), GetFontIndexInFile());

                // calculate the cached data
                try
                {
                    Validator v = GetFile().GetValidator();
                    RasterInterf.UpdateProgressDelegate upg = new RasterInterf.UpdateProgressDelegate(v.OnTableProgress);
                    m_DevMetricsData = ri.CalcDevMetrics(bCalc_hdmx, bCalc_LTSH, bCalc_VDMX,
                        numGlyphs,
                        hdmxPointSizes, maxHdmxPointSize,
                        uchPixelHeightRangeStart, uchPixelHeightRangeEnd,
                        VDMXxResolution, VDMXyResolution, cVDMXResolutions, upg);
                }
                catch (Exception e)
                {
                    m_sDevMetricsDataError = e.Message;
                }
            }

            return m_DevMetricsData;
        }


        public String GetDevMetricsDataError()
        {
            return m_sDevMetricsDataError;
        }




        /******************
         * protected methods
         */
        
        
        protected static OffsetTable ReadOffsetTable(OTFileVal file, uint filepos)
        {
            // read the Offset Table from the file

            Validator v = file.GetValidator();

            const int SIZEOF_OFFSETTABLE = 12;

            OffsetTable ot = null;


            // read the offset table

            MBOBuffer buf = file.ReadPaddedBuffer(filepos, SIZEOF_OFFSETTABLE);

            if (buf != null)
            {
                if (OTFile.IsValidSfntVersion(buf.GetUint(0)))
                {
                    ot = new OffsetTable(buf);
                }
                else
                {
                    v.Error(T.T_NULL, E._OFFSET_E_InvalidSFNT, null, "0x"+buf.GetUint(0).ToString("x8"));
                }
            }


            // now read the directory entries

            if (ot != null)
            {
                const int SIZEOF_DIRECTORYENTRY = 16;


                for (int i=0; i<ot.numTables; i++)
                {
                    uint dirFilePos = (uint)(filepos+SIZEOF_OFFSETTABLE+i*SIZEOF_DIRECTORYENTRY);
                    MBOBuffer DirEntBuf = file.ReadPaddedBuffer(dirFilePos, SIZEOF_DIRECTORYENTRY);

                    if (DirEntBuf != null)
                    {
                        DirectoryEntry de = new DirectoryEntry();

                        de.tag = new OTTag(DirEntBuf.GetBuffer());
                        de.checkSum = DirEntBuf.GetUint(4);
                        de.offset = DirEntBuf.GetUint(8);
                        de.length = DirEntBuf.GetUint(12);

                        ot.DirectoryEntries.Add(de);
                        
                        if (de.offset > file.GetFileLength())
                        {
                            v.Error(T.T_NULL, E._DE_E_OffsetPastEOF, de.tag, "0x"+de.offset.ToString("x8"));
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return ot;
        }

        
        protected bool CheckDirectoryEntriesNonZero(Validator v)
        {
            Debug.Assert(m_OffsetTable != null);

            bool bRet = false;

            if (m_OffsetTable != null)
            {
                bRet = true;

                for (int i = 0; i<m_OffsetTable.DirectoryEntries.Count; i++)
                {
                    DirectoryEntry de = (DirectoryEntry)m_OffsetTable.DirectoryEntries[i];

                    if (de.tag == 0)
                    {
                        v.Error(E._DE_E_TagZero, null);
                        bRet = false;
                    }
                    if (de.length == 0)
                    {
                        v.Error(T.T_NULL, E._DE_E_LengthZero, null, de.tag);
                        bRet = false;
                    }
                    if (de.offset == 0)
                    {
                        v.Error(T.T_NULL, E._DE_E_OffsetZero, null, de.tag);
                        bRet = false;
                    }

                }

            }

            return bRet;
        }


        protected bool CheckNoDuplicateTags(Validator v)
        {
            Debug.Assert(m_OffsetTable != null);

            bool bRet = false;

            if (m_OffsetTable != null)
            {
                bRet = true;

                for (int i = 0; i<m_OffsetTable.DirectoryEntries.Count; i++)
                {
                    DirectoryEntry de1 = (DirectoryEntry)m_OffsetTable.DirectoryEntries[i];

                    // compare this directory entry with every one that follows it
                    for (int j=i+1; j<m_OffsetTable.DirectoryEntries.Count; j++)
                    {
                        DirectoryEntry de2 = (DirectoryEntry)m_OffsetTable.DirectoryEntries[j];

                        if (de1.tag == de2.tag)
                        {
                            v.Error(T.T_NULL, E._DE_E_DuplicateTag, null, de1.tag);
                        }
                    }
                }

            }

            return bRet;
        }

        protected bool CheckTablesInFileAndNotOverlapping(Validator v)
        {
            Debug.Assert(m_OffsetTable != null);

            bool bRet = false;

            if (m_OffsetTable != null)
            {
                bRet = true;

                for (int i = 0; i<m_OffsetTable.DirectoryEntries.Count; i++)
                {
                    DirectoryEntry de = (DirectoryEntry)m_OffsetTable.DirectoryEntries[i];

                    // make sure the offset plus length is within the file
                    if (de.offset + de.length > GetFile().GetFileLength())
                    {
                        v.Error(T.T_NULL, E._DE_E_TableEndPastEOF, de.tag, de.tag);
                        bRet = false;
                    }

                    // make sure the table doesn't overlap any other tables
                    for (int j=0; j<m_OffsetTable.DirectoryEntries.Count; j++)
                    {
                        if (i!=j)    // don't compare the table to itself!
                        {
                            DirectoryEntry de2 = (DirectoryEntry)m_OffsetTable.DirectoryEntries[j];
                            if (
                                // make sure the table doesn't start inside a second table
                                (de.offset > de2.offset && de.offset < de2.offset+de2.length) ||
                                // make sure the table doesn't end inside a second table
                                (de.offset+de.length > de2.offset && de.offset+de.length < de2.offset+de2.length)
                                )
                            {
                                string sDetails = de.tag + " overlaps " + de2.tag;
                                v.Error(T.T_NULL, E._DE_E_OverlappingTable, de.tag, sDetails);
                            }
                        }
                    }
                }

            }

            return bRet;
        }

        protected bool CheckTagsAscending(Validator v)
        {
            Debug.Assert(m_OffsetTable != null);

            bool bRet = false;

            if (m_OffsetTable != null)
            {
                bRet = true;

                for (int i = 0; i<m_OffsetTable.DirectoryEntries.Count; i++)
                {
                    DirectoryEntry de1 = (DirectoryEntry)m_OffsetTable.DirectoryEntries[i];

                    // compare this directory entry with every one that follows it
                    for (int j=i+1; j<m_OffsetTable.DirectoryEntries.Count; j++)
                    {
                        DirectoryEntry de2 = (DirectoryEntry)m_OffsetTable.DirectoryEntries[j];

                        if ((uint)de1.tag > (uint)de2.tag)
                        {
                            v.Error(E._DE_E_TagsAscending, null);
                            bRet = false;
                            break;
                        }
                    }
                    if (bRet == false)
                    {
                        break;
                    }
                }

                if (bRet == true)
                {
                    v.Pass(P._DE_P_TagsAscending, null);
                }
            }

            return bRet;
        }

        protected bool CheckTagNames(Validator v)
        {
            Debug.Assert(m_OffsetTable != null);

            bool bRet = false;

            if (m_OffsetTable != null)
            {
                bRet = true;

                for (int i = 0; i<m_OffsetTable.DirectoryEntries.Count; i++)
                {
                    DirectoryEntry de = (DirectoryEntry)m_OffsetTable.DirectoryEntries[i];

                    if (!de.tag.IsValid())
                    {
                        v.Error(T.T_NULL, E._DE_E_TagName, null, (string)de.tag);
                        bRet = false;
                    }
                }

                if (bRet == true)
                {
                    v.Pass(P._DE_P_TagName, null);
                }

            }

            return bRet;
        }

        protected bool CheckTableAlignment(Validator v)
        {
            Debug.Assert(m_OffsetTable != null);

            bool bRet = false;

            if (m_OffsetTable != null)
            {
                bRet = true;

                for (int i = 0; i<m_OffsetTable.DirectoryEntries.Count; i++)
                {
                    DirectoryEntry de = (DirectoryEntry)m_OffsetTable.DirectoryEntries[i];

                    if ((de.offset & 0x03) != 0)
                    {
                        v.Warning(T.T_NULL, W._DE_W_TableAlignment, null, (string)de.tag);
                        //bRet = false;
                    }
                }

                if (bRet == true)
                {
                    v.Pass(P._DE_P_TableAlignment, null);
                }

            }

            return bRet;
        }

        protected bool CheckForRequiredTables(Validator v)
        {
            bool bRet = true;

            string [] RequiredTables = 
                {"cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post"};

            for (int i=0; i<RequiredTables.Length; i++)
            {
                if (GetDirectoryEntry(RequiredTables[i]) == null)
                {
                    v.Error(T.T_NULL, E._FONT_E_MissingRequiredTable, null, RequiredTables[i]);
                    bRet = false;
                }
            }

            if (GetDirectoryEntry("glyf") == null && GetDirectoryEntry("CFF ") == null && GetDirectoryEntry("EBDT") == null && GetDirectoryEntry("CBDT") == null)
            {
                v.Error(T.T_NULL, E._FONT_E_MissingRequiredTable, null, "Font must contain either a 'glyf', 'CFF ', 'EBDT' or 'CBDT' table");
                bRet = false;
            }

            if (GetDirectoryEntry("glyf") != null)
            {
                string [] RequiredGlyphTables = {"loca"};

                for (int i=0; i<RequiredGlyphTables.Length; i++)
                {
                    if (GetDirectoryEntry(RequiredGlyphTables[i]) == null)
                    {
                        v.Error(T.T_NULL, E._FONT_E_MissingRequiredTable, null, RequiredGlyphTables[i] + " is required since the font contains a glyf table");
                        bRet = false;
                    }
                }
            }

            if (GetDirectoryEntry("EBDT") != null)
            {
                if (GetDirectoryEntry("EBLC") == null)
                {
                    v.Error(T.T_NULL, E._FONT_E_MissingRequiredTable, null, "EBLC is required since the font contains an EBDT table");
                    bRet = false;
                }
            }

            if (GetDirectoryEntry("EBLC") != null)
            {
                if (GetDirectoryEntry("EBDT") == null)
                {
                    v.Error(T.T_NULL, E._FONT_E_MissingRequiredTable, null, "EBDT is required since the font contains an EBLC table");
                    bRet = false;
                }
            }

            if (GetDirectoryEntry("EBSC") != null)
            {
                if (GetDirectoryEntry("EBDT") == null)
                {
                    v.Error(T.T_NULL, E._FONT_E_MissingRequiredTable, null, "EBDT is required since the font contains an EBSC table");
                    bRet = false;
                }
                if (GetDirectoryEntry("EBLC") == null)
                {
                    v.Error(T.T_NULL, E._FONT_E_MissingRequiredTable, null, "EBLC is required since the font contains an EBSC table");
                    bRet = false;
                }
            }


            if (bRet)
            {
                v.Pass(P._FONT_P_MissingRequiredTable, null);
            }

            return bRet;
        }

        protected bool CheckForRecommendedTables(Validator v)
        {
            bool bRet = true;

            bool bMissing = false;

            if (!IsPostScript())
            {

                if (GetDirectoryEntry("gasp") == null)
                {
                    v.Warning(T.T_NULL, W._FONT_W_MissingRecommendedTable, null, "gasp");
                    bMissing = true;
                }

                Table_hmtx hmtxTable = (Table_hmtx)GetTable("hmtx");
                if (hmtxTable != null)
                {
                    if (!hmtxTable.IsMonospace(this) && !ContainsSymbolsOnly())
                    {
                        if (GetDirectoryEntry("hdmx") == null)
                        {
                            v.Warning(T.T_NULL, W._FONT_W_MissingRecommendedTable, null, "hdmx");
                            bMissing = true;
                        }
                    }
                }

                if (GetDirectoryEntry("VDMX") == null)
                {
                    v.Warning(T.T_NULL, W._FONT_W_MissingRecommendedTable, null, "VDMX");
                    bMissing = true;
                }

            }
            
            if (GetDirectoryEntry("DSIG") == null && !GetFile().IsCollection())
            {
                v.Warning(T.T_NULL, W._FONT_W_MissingRecommendedTable, null, "DSIG");
                bMissing = true;
            }

            if (!bMissing)
            {
                v.Pass(P._FONT_P_MissingRecommendedTable, null);
            }

            return bRet;
        }

        protected bool CheckForNoUnnecessaryTables(Validator v)
        {
            bool bRet = true;

            bool bFoundUnnecessary = false;

            Table_hmtx hmtxTable = (Table_hmtx)GetTable("hmtx");
            if (hmtxTable != null)
            {
                if (hmtxTable.IsMonospace(this))
                {
                    if (GetDirectoryEntry("hdmx") != null)
                    {
                        v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "hdmx table not needed for monospaced font");
                        bFoundUnnecessary = true;
                    }

                    if (GetDirectoryEntry("LTSH") != null)
                    {
                        v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "LTSH table not needed for monospaced font");
                        bFoundUnnecessary = true;
                    }

                    if (GetDirectoryEntry("kern") != null)
                    {
                        v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "kern table not needed for monospaced font");
                        bFoundUnnecessary = true;
                    }
                }

                if (GetDirectoryEntry("CFF ") == null && GetDirectoryEntry("VORG") != null)
                {
                    v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "VORG table not needed, it may optionally be present for fonts with Postscript outlines");
                    bFoundUnnecessary = true;
                }

                if (GetDirectoryEntry("PCLT") != null)
                {
                    v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "PCLT table not needed, Microsoft no longer recommends including this table");
                    bFoundUnnecessary = true;
                }
            }

            if (GetDirectoryEntry("CFF ") != null)
            {
                string [] UnnecessaryTables = {"glyf", "fpgm", "cvt ", "loca", "prep"};

                for (int i=0; i<UnnecessaryTables.Length; i++)
                {
                    if (GetDirectoryEntry(UnnecessaryTables[i]) != null)
                    {
                        v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, UnnecessaryTables[i] + " not needed since the font contains a 'CFF ' table");
                        bFoundUnnecessary = true;
                    }
                }
            }

            if (GetDirectoryEntry("glyf") != null)
            {
                if (GetDirectoryEntry("CFF ") != null)
                {
                    v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "CFF not needed since the font contains a 'CFF ' table");
                    bFoundUnnecessary = true;
                }
            }

            if (ContainsLatinOnly())
            {
                if (GetDirectoryEntry("vhea") != null)
                {
                    v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "vhea not needed since the font only contains Latin characters");
                    bFoundUnnecessary = true;
                }

                if (GetDirectoryEntry("vmtx") != null)
                {
                    v.Warning(T.T_NULL, W._FONT_W_UnnecessaryTable, null, "vmtx not needed since the font only contains Latin characters");
                    bFoundUnnecessary = true;
                }
            }


            if (!bFoundUnnecessary)
            {
                v.Pass(P._FONT_P_UnnecessaryTable, null);
            }

            return bRet;
        }

        protected bool CheckForOptimalTableOrder(Validator v)
        {
            bool bRet = true;

            if (!GetFile().IsCollection())  // don't perform this test on .ttc files
            {
                string [] OrderedTables = null;
                string [] TTOrderedTables = 
                {
                    "head", "hhea", "maxp", "OS/2", "hmtx", "LTSH", "VDMX", 
                    "hdmx", "cmap", "fpgm", "prep", "cvt ", "loca", "glyf", 
                    "kern", "name", "post", "gasp", "PCLT" /*"DSIG"*/
                };
                string [] PSOrderedTables = 
                {
                    "head", "hhea", "maxp", "OS/2", "name", "cmap", "post", "CFF "
                };

                if (ContainsTrueTypeOutlines())
                {
                    OrderedTables = TTOrderedTables;
                }
                else if (ContainsPostScriptOutlines())
                {
                    OrderedTables = PSOrderedTables;
                }

                if (OrderedTables != null)
                {
                    Debug.Assert(m_OffsetTable != null);


                    bool bOrderOk = true;

                    if (m_OffsetTable != null)
                    {
                        for (int i=0; i<OrderedTables.Length-1; i++)
                        {
                            for (int j=i+1; j<OrderedTables.Length; j++)
                            {
                                DirectoryEntry deBefore = GetDirectoryEntry(OrderedTables[i]);
                                DirectoryEntry deAfter  = GetDirectoryEntry(OrderedTables[j]);
                                if (deBefore != null && deAfter != null)
                                {
                                    if (deBefore.offset > deAfter.offset)
                                    {
                                        string sDetails = "table '" + deAfter.tag + "' precedes table '" + deBefore.tag + "'";
                                        v.Warning(T.T_NULL, W._FONT_W_OptimalOrder, null, sDetails);
                                        bOrderOk = false;
                                        break;
                                    }
                                }
                            }
                            if (!bOrderOk)
                            {
                                break;
                            }
                        }
                    }

                    if (bOrderOk)
                    {
                        v.Pass(P._FONT_P_OptimalOrder, null);
                    }
                }
            }

            return bRet;
        }

        //Checks the presence of the CFF table to decide whether the font file has or not PostScript outlines
        protected bool IsPostScript()
        {
            DirectoryEntry de_cff = GetDirectoryEntry("CFF ");
            Table_CFF cffTable = (Table_CFF)GetTable("CFF ");
            if (de_cff != null && cffTable != null)
            {
                return true;
            }

            return false;
        }

        //If it is OK, return > 0
        //If it is faulty, return < 0
        //If it has postscript outlines (can't rasterize for now, but it is ok) , return 0
        protected int TestFontRasterization()
        {
            //Any font with postscript outlines instead of truetype outlines has a CFF, table
            //So, if the file has a CFF table, we return 0 promptly
            //
            // FreeType is CFF capable.
            Type freeType = Type.GetType("Compat.OTFontFile.Rasterizer.FreeType.Library");
            if (freeType == null) freeType = Type.GetType("SharpFont.Library");
            if (IsPostScript() && freeType == null)
            {
                m_sDevMetricsDataError = "Font has PostScript outlines, rasterization not yet implemented";
                return 0;
            }

            // We do a sanity check here to make sure font meets minimal requirements before we will do
            // rasterization testing
            string s = "Unable to get data from rasterizer. ";

            DirectoryEntry de_head = GetDirectoryEntry("head");
            Table_head headTable = (Table_head)GetTable("head");
            if (   (de_head != null && headTable == null)
                || headTable == null)
            {
                m_sDevMetricsDataError = s + "'head' table is not present.";
                return -1;
            }
            if (headTable.GetLength() != 54)
            {
                m_sDevMetricsDataError = s + "'head' table length is invalid.";
                return -1;
            }

            if (headTable.magicNumber != 0x5f0f3cf5)
            {
                m_sDevMetricsDataError = s + "'head' table magic number is not correct.";
                return -1;
            }

            Table_maxp maxpTable = (Table_maxp)GetTable("maxp");
            if (maxpTable == null)
            {
                m_sDevMetricsDataError = s + "'maxp' table is not present.";
                return -1;
            }
            uint val = maxpTable.TableVersionNumber.GetUint();
            if (   (val == 0x00005000 && maxpTable.GetLength() != 6)
                || (val == 0x00010000 && maxpTable.GetLength() != 32))
            {
                m_sDevMetricsDataError = s + "'maxp' table length is invalid.";
                return -1;
            }

            DirectoryEntry de_cvt = GetDirectoryEntry("cvt ");
            Table_cvt cvtTable = (Table_cvt)GetTable("cvt ");
            if (de_cvt != null && cvtTable == null)
            {
                m_sDevMetricsDataError = s + "'cvt ' table is not valid.";
                return -1;
            }

            DirectoryEntry de_glyf = GetDirectoryEntry("glyf");
            Table_glyf glyfTable = (Table_glyf)GetTable("glyf");
            if (   (de_glyf != null && glyfTable == null)
                || glyfTable == null)
            {
                m_sDevMetricsDataError = s + "'glyf' table is not valid.";
                return -1;
            }

            Table_hhea hheaTable = (Table_hhea)GetTable("hhea");
            if (hheaTable == null)
            {
                m_sDevMetricsDataError = s + "'hhea' table is not present.";
                return -1;
            }

            return 1;
        }

        /**************
         * member data
         */

        RasterInterf.DevMetricsData m_DevMetricsData;
        String m_sDevMetricsDataError;
    }
}
