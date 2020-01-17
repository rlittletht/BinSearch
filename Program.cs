using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TCore.CmdLine;

namespace BinSearch
{
    class Program
    {
        public static void ParseCmdLine(string[] args, out string sSearch, out string sFile)
        {
            CmdLineConfig cfg = new CmdLineConfig(new CmdLineSwitch[]
            {
                new CmdLineSwitch(null, false, true, "search text", "text to search for", null),
                new CmdLineSwitch(null, false, true, "file to search", "file to search", null),
            });

            CmdLine cmdLine = new CmdLine(cfg);

            string sError;

            if (!cmdLine.FParse(args, null, null, out sError))
            {
                cmdLine.Usage(ConsoleWriteDelegate);
                Environment.Exit(0);
            }

            sSearch = cmdLine.GetPositionalArg(0);
            sFile = cmdLine.GetPositionalArg(1);
        }

        static void ConsoleWriteDelegate(string s)
        {
            Console.WriteLine(s);
        }

        /*----------------------------------------------------------------------------
        	%%Function: SeekToBeginningOfLine
        	%%Qualified: BinSearch.Program.SeekToBeginningOfLine
        	
            seek to the beginning of the current line. never seek forward.

            If the current character is EOF, or if the current character is a line
            ending (or part of a line ending), then skip beyond it to find the start.

            formally, seek the stream such that the PREVIOUS character is either:
             * the beginning of the stream
             * the last byte of a line ending

            recognized line endings:
              0x0a
              0x0d
              0x0d0x0a

            also returns the file pointer to the first char of the line, and the 
            file pointer of the last char of the line (including the EOL character)
        ----------------------------------------------------------------------------*/
        static void SeekToBeginningOfLine(Stream stm)
        {
            long fp = stm.Position;
            byte b, bLast;
            bool fAteLineEnding = false;

            int n = stm.ReadByte();

            // need to read the byte at this position -- if its 0x0a, and the
            // previous character is 0x0d, then we are in the middle of a line ending
            // however, if current is 0x0d and the previous is 0x0d, then its an empty
            // line, and the correct seek location is our current position.

            if (n != -1)
                bLast = (byte)n;
            else
                bLast = 0;  // doesn't matter what this is since we just want to make sure we don't think we are in the middle of a line ending.

            while (fp > 0)
            {
                stm.Seek(fp - 1, SeekOrigin.Begin);

                b = (byte) stm.ReadByte();

                switch (b)
                {
                    case (byte) 0x0d:
                    {
                        if (bLast == 0x0d)
                        {
                            // b represents a line ending, so we want to return the current position
                            stm.Seek(fp, SeekOrigin.Begin);
                            return;
                        }

                        if (bLast == 0x0a && fAteLineEnding)
                        {
                            // bLast represents a line ending, so we want to return the current position + 1
                            stm.Seek(fp + 1, SeekOrigin.Begin);
                            return;
                        }

                        if (bLast != 0x0a)
                        {
                            // b represents a line ending, return fp
                            stm.Seek(fp, SeekOrigin.Begin);
                            return;
                        }
                        break;
                    }

                    case (byte) 0x0a:
                    {
                        // 0x0a is always a line ending -- either on its own, or combined with 0x0d. so the current position
                        // is the right one.
                        stm.Seek(fp, SeekOrigin.Begin);
                        return;
                    }
                }
                fAteLineEnding = true;
                fp--;
            }
            // if we get here, we got to the beginning of the file...return that
            stm.Seek(0, SeekOrigin.Begin);
        }

        static string ReadLineAroundCurrentPoint(Stream stm, out long fpFirst, out long fpLineLast)
        {
            SeekToBeginningOfLine(stm);

            fpFirst = stm.Position;
            fpLineLast = -1;
            long fpLast = -1;

            bool fParsingCR = false;

            // read char by char
            int n;
            while ((n = stm.ReadByte()) != -1)
            {
                byte b = (byte) n;
                if (b == 0x0d)
                {
                    if (fParsingCR)
                    {
                        fpLast = stm.Position - 2;
                        fpLineLast = stm.Position - 1;
                        break;
                    }

                    fParsingCR = true;
                }
                else if (b == 0x0a)
                {
                    if (fParsingCR)
                    {
                        fpLast = stm.Position - 3;
                        fpLineLast = stm.Position - 1;
                    }
                    else
                    {
                        fpLast = stm.Position - 2;
                        fpLineLast = stm.Position - 1;
                    }

                    break;
                }
                else if (fParsingCR)
                {
                    fpLast = stm.Position - 2;
                    fpLineLast = stm.Position - 1;
                    break;
                }
            }

            if (fpLast == -1)
            {
                if (fParsingCR)
                {
                    // we *did* see the line ending, but we were looking for a possible LF
                    fpLast = stm.Length - 2;
                    fpLineLast = stm.Length - 1;
                }
                else
                {
                    fpLineLast = fpLast = stm.Length - 1;
                }
            }

            byte[] rgb = new byte[fpLast - fpFirst + 1];

            stm.Seek(fpFirst, SeekOrigin.Begin);
            stm.Read(rgb, 0, (int) (fpLast - fpFirst + 1));

            return Encoding.UTF8.GetString(rgb);
        }

        static string FindLineInRange(Stream stm, long fpFirst, long fpLast, string sSearch)
        {
            if (fpFirst >= fpLast)
                return null;

            // first, find the midpoint of the range
            long fpMid = fpFirst + (fpLast - fpFirst) / 2;

            // seek to that location
            stm.Seek(fpMid, SeekOrigin.Begin);

            // now read the line around this point
            string sLine = ReadLineAroundCurrentPoint(stm, out long fpLineFirst, out long fpLineLast);

            int nCmp = string.CompareOrdinal(sLine.Substring(0, Math.Min(sLine.Length, sSearch.Length)).ToUpper(), sSearch);

            if (nCmp == 0)
                return sLine;

            if (nCmp > 0)
                return FindLineInRange(stm, fpFirst, fpLineFirst, sSearch);

            return FindLineInRange(stm, fpLineLast + 1, fpLast, sSearch);
        }

        static void SearchFile(string sSearch, string sFile)
        {
            using (FileStream stm = new FileStream(sFile, FileMode.Open))
            {
                long fpFirst = 0;
                long fpLast = stm.Length;

                string sLine = FindLineInRange(stm, fpFirst, fpLast, sSearch.ToUpper());
                if (sLine == null)
                    Console.WriteLine($"{sSearch} not found in file {sFile}.");
                else
                    Console.WriteLine($"{sSearch} found: {sLine}.");

                stm.Close();
            }
        }

        static void Main(string[] args)
        {
            DoUnitTests();
            ParseCmdLine(args, out string sSearch, out string sFile);

            SearchFile(sSearch, sFile);
        }

        static void TestFindLineInRange_ExactMatch()
        {
            string sSearch = "12345";
            DebugStream stm = DebugStream.StmCreateFromString(sSearch);

            string sLine = FindLineInRange(stm, 0, stm.Length, sSearch);

            Debug.Assert(sLine == sSearch);
        }

        static void TestFindLineInRange_FirstHalf_Found()
        {
            string sFile = "12\n34\n";
            string sSearch = "12";

            DebugStream stm = DebugStream.StmCreateFromString(sFile);

            string sLine = FindLineInRange(stm, 0, stm.Length, sSearch);

            Debug.Assert(sLine == sSearch);
        }

        static void TestFindLineInRange_FirstHalf_NotFound()
        {
            string sFile = "12\n34\n";
            string sSearch = "13";

            DebugStream stm = DebugStream.StmCreateFromString(sFile);

            string sLine = FindLineInRange(stm, 0, stm.Length, sSearch);

            Debug.Assert(sLine == null);
        }

        static void TestFindLineInRange_OtherHalf_Found()
        {
            string sFile = "12\n34\n";
            string sSearch = "34";

            DebugStream stm = DebugStream.StmCreateFromString(sFile);

            string sLine = FindLineInRange(stm, 0, stm.Length, sSearch);

            Debug.Assert(sLine == sSearch);
        }

        static void TestFindLineInRange_OtherHalf_NotFound()
        {
            string sFile = "12\n34\n";
            string sSearch = "32";

            DebugStream stm = DebugStream.StmCreateFromString(sFile);

            string sLine = FindLineInRange(stm, 0, stm.Length, sSearch);

            Debug.Assert(sLine == null);
        }

        static void TestFindLineInRange_FirstHalfMuchSmaller_Found()
        {
            string sFile = "12\n34567890123456789\n";
            string sSearch = "12";

            DebugStream stm = DebugStream.StmCreateFromString(sFile);

            string sLine = FindLineInRange(stm, 0, stm.Length, sSearch);

            Debug.Assert(sLine == sSearch);
        }

        static void TestFindLineInRange_FirstHalfMuchSmaller2_Found()
        {
            string sFile = "12\n34567890123456789\n5456789012345678934567890123456789\n34567890123456789\n34567890123456789\n\n";
            string sSearch = "12";

            DebugStream stm = DebugStream.StmCreateFromString(sFile);

            string sLine = FindLineInRange(stm, 0, stm.Length, sSearch);

            Debug.Assert(sLine == sSearch);
        }

        #region Test SeekToBeginningOfLIne

        static long CallAndReturnSeekToBeginningOfLine(Stream stm, long fpStart)
        {
            stm.Seek(fpStart, SeekOrigin.Begin);
            SeekToBeginningOfLine(stm);

            return stm.Position;
        }

        static void TestSeekToBeginningOfLine_AlreadyAtStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 0) == 0);
        }

        static void TestSeekToBeginningOfLine_MidLine_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 2) == 0);
        }

        static void TestSeekToBeginningOfLine_EndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 5) == 0);
        }

        static void TestSeekToBeginningOfLine_BeforeCrLfAtEndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 4) == 0);
        }

        static void TestSeekToBeginningOfLine_BeforeCrAtEndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240a");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 4) == 0);
        }

        static void TestSeekToBeginningOfLine_BeforeLfAtEndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240d");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 4) == 0);
        }

        static void TestSeekToBeginningOfLine_AtCrLfAtEndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 5) == 0);
        }

        static void TestSeekToBeginningOfLine_AtCrAtEndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240a");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 5) == 0);
        }

        static void TestSeekToBeginningOfLine_AtLfAtEndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240d");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 5) == 0);
        }

        static void TestSeekToBeginningOfLine_WithinCrLfAtEndOfFile_ShouldSeekToStartOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 6) == 0);
        }

        static void TestSeekToBeginningOfLine_AfterCrLfAtEndOfFile_ShouldSeekToEndOfFile()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 7) == 7);
        }

        static void TestSeekToBeginningOfLine_AlreadyAtStartOfLineCrLf()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 7) == 7);
        }

        static void TestSeekToBeginningOfLine_AlreadyAtStartOfLineCr()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240a12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 6) == 6);
        }

        static void TestSeekToBeginningOfLine_AlreadyAtStartOfLineLf()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240d12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 6) == 6);
        }

        // test empty line with all types of endings on either side...
        static void TestSeekToBeginningOfLine_MidLine_ShouldSeekToStartOfLine()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 9) == 7);
        }

        static void TestSeekToBeginningOfLine_EndOfFile_ShouldSeekToStartOfLine()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n12345");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 12) == 7);
        }

        static void TestSeekToBeginningOfLine_BeforeCrLfAtEndOfFile_ShouldSeekToStartOfLine()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 4) == 0);
        }

        // now, do we handle empty lines well?

        static void TestSeekToBeginningOfLine_AtEndOfLineCrAfterEndOfLineCr_ShouldSeekToStartOfCurrentLine()
        {
            DebugStream stm = DebugStream.StmCreateFromString("\x240a\x240a");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 1) == 1);
        }

        static void TestSeekToBeginningOfLine_AtEndOfLineLfAfterEndOfLineLf_ShouldSeekToStartOfCurrentLine()
        {
            DebugStream stm = DebugStream.StmCreateFromString("\x240d\x240d");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 1) == 1);
        }

        static void TestSeekToBeginningOfLine_AtEndOfLineCrLfAfterEndOfLineCrLf_ShouldSeekToStartOfCurrentLine()
        {
            DebugStream stm = DebugStream.StmCreateFromString("\n\n");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 2) == 2);
        }

        static void TestSeekToBeginningOfLine_WithinEndOfLineCrLfAfterEndOfLineCrLf_ShouldSeekToStartOfCurrentLine()
        {
            DebugStream stm = DebugStream.StmCreateFromString("\n\n");

            Debug.Assert(CallAndReturnSeekToBeginningOfLine(stm, 3) == 2);
        }
        #endregion

        #region Test ReadLineAround
        static void TestReadLineAround_AlreadyAtStartOfLine_NoLineEnding()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n12345");
            stm.Seek(7, SeekOrigin.Begin);

            string sLine = ReadLineAroundCurrentPoint(stm, out long fpFirst, out long fpLast);

            Debug.Assert(sLine == "12345");
            Debug.Assert(fpFirst == 7);
            Debug.Assert(fpLast == 11);
        }

        static void TestReadLineAround_AlreadyAtStartOfLine_EndingCrLf()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\n12345\n");
            stm.Seek(7, SeekOrigin.Begin);

            string sLine = ReadLineAroundCurrentPoint(stm, out long fpFirst, out long fpLast);

            Debug.Assert(sLine == "12345");
            Debug.Assert(fpFirst == 7);
            Debug.Assert(fpLast == 13);
        }

        static void TestReadLineAround_AlreadyAtStartOfLine_EndingCr()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240a12345\x240a");
            stm.Seek(7, SeekOrigin.Begin);

            string sLine = ReadLineAroundCurrentPoint(stm, out long fpFirst, out long fpLast);

            Debug.Assert(sLine == "12345");
            Debug.Assert(fpFirst == 6);
            Debug.Assert(fpLast == 11);
        }

        static void TestReadLineAround_AlreadyAtStartOfLine_EndingLf()
        {
            DebugStream stm = DebugStream.StmCreateFromString("12345\x240d12345\x240d");
            stm.Seek(7, SeekOrigin.Begin);

            string sLine = ReadLineAroundCurrentPoint(stm, out long fpFirst, out long fpLast);

            Debug.Assert(sLine == "12345");
            Debug.Assert(fpFirst == 6);
            Debug.Assert(fpLast == 11);
        }
        #endregion


        static void DoUnitTests()
        {
            CmdLine.UnitTest();
            TestFindLineInRange_ExactMatch();
            TestFindLineInRange_FirstHalf_Found();
            TestFindLineInRange_FirstHalf_NotFound();
            TestFindLineInRange_OtherHalf_Found();
            TestFindLineInRange_OtherHalf_NotFound();
            TestFindLineInRange_FirstHalfMuchSmaller_Found();
            TestFindLineInRange_FirstHalfMuchSmaller2_Found();

            TestSeekToBeginningOfLine_AlreadyAtStartOfFile();
            TestSeekToBeginningOfLine_MidLine_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_EndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_BeforeCrLfAtEndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_BeforeCrAtEndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_BeforeLfAtEndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_AtCrLfAtEndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_AtCrAtEndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_AtLfAtEndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_WithinCrLfAtEndOfFile_ShouldSeekToStartOfFile();
            TestSeekToBeginningOfLine_AfterCrLfAtEndOfFile_ShouldSeekToEndOfFile();

            TestSeekToBeginningOfLine_AlreadyAtStartOfLineCrLf();
            TestSeekToBeginningOfLine_AlreadyAtStartOfLineCr();
            TestSeekToBeginningOfLine_AlreadyAtStartOfLineLf();
            TestSeekToBeginningOfLine_MidLine_ShouldSeekToStartOfLine();
            TestSeekToBeginningOfLine_EndOfFile_ShouldSeekToStartOfLine();
            TestSeekToBeginningOfLine_BeforeCrLfAtEndOfFile_ShouldSeekToStartOfLine();

            TestSeekToBeginningOfLine_AtEndOfLineCrAfterEndOfLineCr_ShouldSeekToStartOfCurrentLine();
            TestSeekToBeginningOfLine_AtEndOfLineCrLfAfterEndOfLineCrLf_ShouldSeekToStartOfCurrentLine();
            TestSeekToBeginningOfLine_WithinEndOfLineCrLfAfterEndOfLineCrLf_ShouldSeekToStartOfCurrentLine();
            TestSeekToBeginningOfLine_AtEndOfLineLfAfterEndOfLineLf_ShouldSeekToStartOfCurrentLine();

            TestReadLineAround_AlreadyAtStartOfLine_NoLineEnding();
            TestReadLineAround_AlreadyAtStartOfLine_EndingCrLf();
            TestReadLineAround_AlreadyAtStartOfLine_EndingCr();
            TestReadLineAround_AlreadyAtStartOfLine_EndingLf();
        }
    }
}
