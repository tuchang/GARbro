﻿//! \file       ArcNOA.cs
//! \date       Thu Apr 23 15:57:17 2015
//! \brief      Entis GLS engine archives implementation.
//
// Copyright (C) 2015 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Text;
using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    internal class NoaOptions : ResourceOptions
    {
        public string     Scheme { get; set; }
        public string PassPhrase { get; set; }
    }

    internal class NoaEntry : Entry
    {
        public byte[]   Extra;
        public uint     Encryption;
        public uint     Attr;
    }

    internal class NoaArchive : ArcFile
    {
        public string Password;

        public NoaArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, string password = null)
            : base (arc, impl, dir)
        {
            Password = password;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class NoaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NOA"; } }
        public override string Description { get { return "Entis GLS engine resource archive"; } }
        public override uint     Signature { get { return 0x69746e45; } } // 'Enti'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public NoaOpener ()
        {
            Extensions = new string[] { "noa", "dat" };
        }

        public static readonly Dictionary<string, Dictionary<string, string>> KnownKeys =
            new Dictionary<string, Dictionary<string, string>> {
                { arcStrings.NOAIgnoreEncryption, new Dictionary<string, string>() },
                { "Alea Akaki Tsuki o Haruka ni Nozomi", new Dictionary<string, string> {
                    { "data2.noa", "pnnAiYVqktMdLlVq9pnrXs1795vhu8ZluLh3MxmXyBBrhrhLoP2rlGn5dxcBP6d1cAAz08TMRIXNUFatVdJFWAwVphtAh4hx5NHMmLs8LoBE2KHAA8GnKJB1PpKeyMHu" },
                    { "data4.noa", "yEbgydEFtIq3YiGUNMpCarJwR9mZbufPrbXtsoqbrJwT4F278kOWIgYzLtm1nP1Hns81u3F4Stwc42gdtrWIbnp9XfX3LsKiZe1TFUyrlTqsbhX8R8dEAVxLk9SVvCE7" } } },
                { "Do S Ane to Boku no Hounyou Kankei", new Dictionary<string, string> {
                    { "d02.dat", "vwerc7s65r21bnfu" },
                    { "d03.dat", "ctfvgbhnj67y8u" } } },
                { "Innyuu Famiresu", new Dictionary<string, string> {
                    { "d01.dat", "vdiu$43AfUCfh9aksf" },
                    { "d03.dat", "gaivnwq7365e021gf" } } },
                { "Konneko", new Dictionary<string, string> {
                    { "script.noa", "convini_cat" } } },
                { "Yatohime Zankikou", new Dictionary<string, string> {
                    { "data1.noa", "arcdatapass" },
                    { "data6.noa", "cfe7231hf9qccda" },
                    { "data7.noa", "ceiuvw86680efq0hHDUHF673j" } } },
                { "You! Apron Chakuyou", new Dictionary<string, string> {
                    { "containerb.noa", "7DQ1Xm7ZahIv1ZwlFgyMTMryKC6OP9V6cAgL64WD5JLyvmeEyqTSA5rUbRigOtebnnK4MuOptwsbOf4K8UBDH4kpAUOQgB71Qr1qxtHGxQl8KZKj6WIYWpPh0G3JOJat" } } },
            };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "Entis\x1a"))
                return null;
            uint id = file.View.ReadUInt32 (8);
            if (0x02000400 != id)
                return null;
            var reader = new IndexReader (file);
            if (!reader.ParseRoot() || 0 == reader.Dir.Count)
                return null;
            if (!reader.HasEncrypted)
                return new ArcFile (file, this, reader.Dir);

            var options = Query<NoaOptions> (arcStrings.ArcEncryptedNotice);
            string password = null;
            if (!string.IsNullOrEmpty (options.PassPhrase))
            {
                password = options.PassPhrase;
            }
            else if (!string.IsNullOrEmpty (options.Scheme))
            {
                Dictionary<string, string> filemap;
                if (KnownKeys.TryGetValue (options.Scheme, out filemap))
                {
                    var filename = Path.GetFileName (file.Name).ToLowerInvariant();
                    filemap.TryGetValue (filename, out password);
                }
            }
            if (string.IsNullOrEmpty (password))
                return new ArcFile (file, this, reader.Dir);
            return new NoaArchive (file, this, reader.Dir, password);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nent = entry as NoaEntry;
            if (null == nent || !arc.File.View.AsciiEqual (entry.Offset, "filedata"))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            ulong size = arc.File.View.ReadUInt64 (entry.Offset+8);
            if (size > int.MaxValue)
                throw new FileSizeException();
            if (0 == size)
                return Stream.Null;

            var input = arc.File.CreateStream (entry.Offset+0x10, (uint)size);
            try
            {
                var narc = arc as NoaArchive;
                if (0 == nent.Encryption || size < 4 || null == narc || null == narc.Password)
                    return input;
                if (0x40000000 != nent.Encryption)
                {
                    Trace.WriteLine (string.Format ("{0}: unknown encryption scheme 0x{1:x8}",
                                                    nent.Name, nent.Encryption));
                    return input;
                }
                uint nTotalBytes = (uint)(size - 4);
                var pBSHF = new BSHFDecodeContext (0x10000);
                pBSHF.AttachInputFile (input);
                pBSHF.PrepareToDecodeBSHFCode (narc.Password);

                byte[] buf = new byte[nTotalBytes];
                uint decoded = pBSHF.DecodeBSHFCodeBytes (buf, nTotalBytes);
                if (decoded < nTotalBytes)
                    throw new EndOfStreamException ("Unexpected end of encrypted stream");

                /* Something wrong with preceding length calculation, resulting CRC doesn't match
                byte[] bufCRC = new byte[4];
                int iCRC = 0;
                for (int i = 0; i < buf.Length; ++i)
                {
                    bufCRC[iCRC] ^= buf[i];
                    iCRC = (iCRC + 1) & 0x03;
                }
                uint orgCRC = arc.File.View.ReadUInt32 (entry.Offset+0x10+nTotalBytes);
                uint crc = LittleEndian.ToUInt32 (bufCRC, 0);
                if (orgCRC != crc)
                {
                    Trace.WriteLine (string.Format ("{0}: CRC mismatch", nent.Name));
                    input.Position = 0;
                    return input;
                }
                */
                input.Dispose();
                return new MemoryStream (buf);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new NoaOptions {
                Scheme = Settings.Default.NOAScheme,
                PassPhrase = Settings.Default.NOAPassPhrase,
            };
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetNOA();
        }

        internal class IndexReader
        {
            ArcView     m_file;
            List<Entry> m_dir = new List<Entry>();
            bool        m_found_encrypted = false;

            const char PathSeparatorChar = '/';

            public List<Entry> Dir { get { return m_dir; } }

            public bool HasEncrypted { get { return m_found_encrypted; } }

            public IndexReader (ArcView file)
            {
                m_file = file;
            }

            public bool ParseRoot ()
            {
                return ParseDirEntry (0x40, "");
            }

            private bool ParseDirEntry (long dir_offset, string cur_dir)
            {
                if (!m_file.View.AsciiEqual (dir_offset, "DirEntry"))
                    return false;
                long size = m_file.View.ReadInt64 (dir_offset+8);
                if (size <= 0 || size > int.MaxValue)
                    return false;
                if ((uint)size > m_file.View.Reserve (dir_offset+8, (uint)size))
                    return false;
                long base_offset = dir_offset;
                dir_offset += 0x10;
                int count = m_file.View.ReadInt32 (dir_offset);
                dir_offset += 4;
                if (m_dir.Capacity < m_dir.Count+count)
                    m_dir.Capacity = m_dir.Count+count;
                for (int i = 0; i < count; ++i)
                {
                    var entry = new NoaEntry();
                    entry.Size = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 8;

                    entry.Attr = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 4;

                    entry.Encryption = m_file.View.ReadUInt32 (dir_offset);
                    if (0 != entry.Encryption)
                        m_found_encrypted = true;
                    dir_offset += 4;

                    entry.Offset = base_offset + m_file.View.ReadInt64 (dir_offset);
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                    {
                        entry.Size = (uint)(m_file.MaxOffset - entry.Offset);
                    }
                    dir_offset += 0x10;

                    uint extra_length = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 4;
                    if (extra_length > 0 && 0 == (entry.Attr & 0x70))
                    {
                        entry.Extra = new byte[extra_length];
                        if (entry.Extra.Length != m_file.View.Read (dir_offset, entry.Extra, 0, extra_length))
                            return false;
                    }
                    dir_offset += extra_length;
                    uint name_length = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 4;

                    string name = m_file.View.ReadString (dir_offset, name_length);
                    dir_offset += name_length;

                    if (string.IsNullOrEmpty (cur_dir))
                        entry.Name = name;
                    else
                        entry.Name = cur_dir + PathSeparatorChar + name;
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                    if (0x10 == entry.Attr)
                    {
                        if (!ParseDirEntry (entry.Offset+0x10, entry.Name))
                            return false;
                    }
                    else if (0x20 == entry.Attr || 0x40 == entry.Attr)
                    {
                        break;
                    }
                    else
                    {
                        m_dir.Add (entry);
                    }
                }
                return true;
            }
        }
    }

    internal abstract class ERISADecodeContext
    {
        protected int       m_nIntBufCount;
        protected uint      m_dwIntBuffer;
        protected uint      m_nBufferingSize;
        protected uint      m_nBufCount;
        protected byte[]    m_ptrBuffer;
        protected int       m_ptrNextBuf;

        protected Stream    m_pFile;
        protected ERISADecodeContext m_pContext;

        public ERISADecodeContext (uint nBufferingSize)
        {
            m_nIntBufCount = 0;
            m_nBufferingSize = (nBufferingSize + 0x03) & ~0x03u;
            m_nBufCount = 0;
            m_ptrBuffer = new byte[nBufferingSize];
            m_pFile = null;
            m_pContext = null;
        }

        public void AttachInputFile (Stream file)
        {
            m_pFile = file;
            m_pContext = null;
        }

        public void AttachInputContext (ERISADecodeContext context)
        {
            m_pFile = null;
            m_pContext = context;
        }

        public uint ReadNextData (byte[] ptrBuffer, uint nBytes)
        {
            if (m_pFile != null)
            {
                return (uint)m_pFile.Read (ptrBuffer, 0, (int)nBytes);
            }
            else if (m_pContext != null)
            {
                return m_pContext.DecodeBytes (ptrBuffer, nBytes);
            }
            else
            {
                throw new ApplicationException ("Uninitialized ERISA encryption context");
            }
        }

        public abstract uint DecodeBytes (Array ptrDst, uint nCount);

        protected bool PrefetchBuffer()
        {
            if (0 == m_nIntBufCount)
            {
                if (0 == m_nBufCount)
                {
                    m_ptrNextBuf = 0; // m_ptrBuffer;
                    m_nBufCount = ReadNextData (m_ptrBuffer, m_nBufferingSize);
                    if (0 == m_nBufCount)
                    {
                        return false;
                    }
                    if (0 != (m_nBufCount & 0x03))
                    {
                        uint    i = m_nBufCount;
                        m_nBufCount += 4 - (m_nBufCount & 0x03);
                        while (i < m_nBufCount)
                            m_ptrBuffer[i ++] = 0;
                    }
                }
                m_nIntBufCount = 32;
                m_dwIntBuffer =
                      ((uint)m_ptrBuffer[m_ptrNextBuf] << 24) | ((uint)m_ptrBuffer[m_ptrNextBuf+1] << 16)
                    | ((uint)m_ptrBuffer[m_ptrNextBuf+2] << 8) | (uint)m_ptrBuffer[m_ptrNextBuf+3];
                m_ptrNextBuf += 4;
                m_nBufCount -= 4;
            }
            return true;
        }

        public void FlushBuffer ()
        {
            m_nIntBufCount = 0;
            m_nBufCount = 0;
        }

        public int GetABit ()
        {
            if (!PrefetchBuffer())
            {
                return  1;
            }
            int nValue = ((int)m_dwIntBuffer) >> 31;
            --m_nIntBufCount;
            m_dwIntBuffer <<= 1;
            return nValue;
        }

        public uint GetNBits (int n)
        {
            uint nCode = 0;
            while (n != 0)
            {
                if (!PrefetchBuffer())
                    break;

                int nCopyBits = Math.Min (n, m_nIntBufCount);
                nCode = (nCode << nCopyBits) | (m_dwIntBuffer >> (32 - nCopyBits));
                n -= nCopyBits;
                m_nIntBufCount -= nCopyBits;
                m_dwIntBuffer <<= nCopyBits;
            }
            return nCode;
        }
    }

    internal class BSHFDecodeContext : ERISADecodeContext
    {
        ERIBshfBuffer   m_pBshfBuf;
        uint            m_dwBufPos;

        public BSHFDecodeContext (uint nBufferingSize) : base (nBufferingSize)
        {
            m_pBshfBuf = null;
        }

        public void PrepareToDecodeBSHFCode (string pszPassword)
        {
            if (null == m_pBshfBuf)
            {
                m_pBshfBuf = new ERIBshfBuffer();
            }
            if (string.IsNullOrEmpty (pszPassword))
            {
                pszPassword = " ";
            }
            int char_count = Encoding.ASCII.GetByteCount (pszPassword);
            int length = Math.Max (char_count, 32);
            var pass_bytes = new byte[length];
            char_count = Encoding.ASCII.GetBytes (pszPassword, 0, pszPassword.Length, pass_bytes, 0);
            if (char_count < 32)
            {
                pass_bytes[char_count++] = 0x1b;
                for (int i = char_count; i < 32; ++i)
                {
                    pass_bytes[i] = (byte)(pass_bytes[i % char_count] + pass_bytes[i - 1]);
                }
            }
            m_pBshfBuf.m_strPassword = pass_bytes;
            m_pBshfBuf.m_dwPassOffset = 0;
            m_dwBufPos = 32;
        }

        public override uint DecodeBytes (Array ptrDst, uint nCount)
        {
            return DecodeBSHFCodeBytes (ptrDst as byte[], nCount);
        }

        public uint DecodeBSHFCodeBytes (byte[] ptrDst, uint nCount)
        {
            uint nDecoded = 0;
            while (nDecoded < nCount)
            {
                if (m_dwBufPos >= 32)
                {
                    for (int i = 0; i < 32; ++i)
                    {
                        if (0 == m_nBufCount)
                        {
                            m_ptrNextBuf = 0;
                            m_nBufCount = ReadNextData (m_ptrBuffer, m_nBufferingSize);
                            if (0 == m_nBufCount)
                            {
                                return nDecoded;
                            }
                        }
                        m_pBshfBuf.m_srcBSHF[i] = m_ptrBuffer[m_ptrNextBuf++];
                        m_nBufCount--;
                    }
                    m_pBshfBuf.DecodeBuffer();
                    m_dwBufPos = 0;
                }
                ptrDst[nDecoded++] = m_pBshfBuf.m_bufBSHF[m_dwBufPos++];
            }
            return nDecoded;
        }
    }

    internal class ERIBshfBuffer
    {
        public byte[]   m_strPassword;
        public uint     m_dwPassOffset = 0;
        public byte[]   m_bufBSHF = new byte[32];
        public byte[]   m_srcBSHF = new byte[32];
        public byte[]   m_maskBSHF = new byte[32];

        public void DecodeBuffer ()
        {
            int nPassLen = m_strPassword.Length;
            if ((int)m_dwPassOffset >= nPassLen)
            {
                m_dwPassOffset = 0;
            }
            for (int i = 0; i < 32; ++i)
            {
                m_bufBSHF[i]  = 0;
                m_maskBSHF[i] = 0;
            }
            int iPos = (int) m_dwPassOffset++;
            int iBit = 0;
            for (int i = 0; i < 256; ++i)
            {
                iBit = (iBit + m_strPassword[iPos++]) & 0xFF;
                if (iPos >= nPassLen)
                {
                    iPos = 0;
                }
                int iOffset = (iBit >> 3);
                int iMask = (0x80 >> (iBit & 0x07));
                while (0xFF == m_maskBSHF[iOffset])
                {
                    iBit = (iBit + 8) & 0xFF;
                    iOffset = (iBit >> 3);
                }
                while (0 != (m_maskBSHF[iOffset] & iMask))
                {
                    iBit ++;
                    iMask >>= 1;
                    if (0 == iMask)
                    {
                        iBit = (iBit + 8) & 0xFF;
                        iOffset = (iBit >> 3);
                        iMask = 0x80;
                    }
                }
                Debug.Assert (iMask != 0);
                m_maskBSHF[iOffset] |= (byte) iMask;

                if (0 != (m_srcBSHF[(i >> 3)] & (0x80 >> (i & 0x07))))
                {
                    m_bufBSHF[iOffset] |= (byte)iMask;
                }
            }
        }
    }
}