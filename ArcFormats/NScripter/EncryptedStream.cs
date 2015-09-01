//! \file       ArcEncrypted.cs
//! \date       Mon Aug 31 11:43:25 2015
//! \brief      Encrypted NSA archives implementation.
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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.NScripter
{
    public class ViewStream : Stream
    {
        protected ArcView   m_file;
        protected long      m_position = 0;
        bool                m_should_dispose;

        public ViewStream (ArcView mmap, bool leave_open = false)
        {
            m_file = mmap;
            m_should_dispose = !leave_open;
        }

        public override int Read (byte[] buf, int index, int count)
        {
            if (m_position >= m_file.MaxOffset)
                return 0;
            int read = m_file.View.Read (m_position, buf, index, (uint)count);
            m_position += read;
            return read;
        }

        #region IO.Stream methods
        public override bool  CanRead { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override bool  CanSeek { get { return true; } }

        public override long Length { get { return m_file.MaxOffset; } }
        public override long Position
        {
            get { return m_position; }
            set { m_position = value; }
        }

        public override long Seek (long pos, SeekOrigin whence)
        {
            if (SeekOrigin.Current == whence)
                m_position += pos;
            else if (SeekOrigin.End == whence)
                m_position = m_file.MaxOffset + pos;
            else
                m_position = pos;
            return m_position;
        }

        public override void Write (byte[] buf, int index, int count)
        {
            throw new NotSupportedException();
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException();
        }

        public override void Flush ()
        {
        }
        #endregion

        #region IDisposable methods
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing && m_should_dispose)
                    m_file.Dispose();
                m_disposed = true;
                base.Dispose();
            }
        }
        #endregion
    }

    internal class EncryptedViewStream : ViewStream
    {
        byte[]          m_key;
        byte[]          m_current_block = new byte[BlockLength];
        int             m_current_block_length = 0;
        long            m_current_block_position = 0;

        static readonly HashAlgorithm MD5  = System.Security.Cryptography.MD5.Create();
        static readonly HashAlgorithm SHA1 = System.Security.Cryptography.SHA1.Create();

        public const int BlockLength = 1024;

        public EncryptedViewStream (ArcView mmap, byte[] key, bool leave_open = false)
            : base (mmap, leave_open)
        {
            m_key = key;
        }

        public override int Read (byte[] buf, int index, int count)
        {
            int total_read = 0;
            bool refill_buffer = !(m_position >= m_current_block_position && m_position < m_current_block_position + m_current_block_length);
            while (count > 0 && m_position < m_file.MaxOffset)
            {
                if (refill_buffer)
                {
                    int block_num = (int)(m_position / BlockLength);
                    m_current_block_position = m_position & ~((long)BlockLength-1);
                    m_current_block_length = m_file.View.Read (m_current_block_position, m_current_block, 0, (uint)BlockLength);
                    DecryptBlock (block_num);
                }
                int src_offset = (int)m_position & (BlockLength-1);
                int available = Math.Min (count, m_current_block_length - src_offset);
                Buffer.BlockCopy (m_current_block, src_offset, buf, index, available);
                m_position += available;
                total_read += available;
                index += available;
                count -= available;
                refill_buffer = true;
            }
            return total_read;
        }

        private void DecryptBlock (int block_num)
        {
            byte[] bn = new byte[8];
            LittleEndian.Pack (block_num, bn, 0);

            var md5_hash = MD5.ComputeHash (bn);
            var sha1_hash = SHA1.ComputeHash (bn);
            var hmac_key = new byte[16];
            for (int i = 0; i < 16; i++)
                hmac_key[i] = (byte)(md5_hash[i] ^ sha1_hash[i]);

            var HMAC = new HMACSHA512 (hmac_key);
            var hmac_hash = HMAC.ComputeHash (m_key);

            int[] map = Enumerable.Range (0, 256).ToArray();

            byte index = 0;
            int h = 0;
            for (int i = 0; i < 256; i++)
            {
                if (hmac_hash.Length == h)
                    h = 0;
                int tmp = map[i];
                index = (byte)(tmp + hmac_hash[h++] + index);
                map[i] = map[index];
                map[index] = tmp;
            }

            int i0 = 0, i1 = 0;
            for (int i = 0; i < 300; i++)
            {
                i0 = (i0 + 1) & 0xFF;
                int tmp = map[i0];
                i1 = (i1 + tmp) & 0xFF;
                map[i0] = map[i1];
                map[i1] = tmp;
            }

            for (int i = 0; i < m_current_block_length; i++)
            {
                i0 = (i0 + 1) & 0xFF;
                int tmp = map[i0];
                i1 = (i1 + tmp) & 0xFF;
                map[i0] = map[i1];
                map[i1] = tmp;
                m_current_block[i] ^= (byte)map[(map[i0] + tmp) & 0xFF];
            }
        }
    }
}