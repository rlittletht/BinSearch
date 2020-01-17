
using System;
using System.IO;
using System.Text;

namespace BinSearch
{
    public class DebugStream : Stream
    {
        private byte[] m_rgbData;
        private long m_cbStream;
        private long m_ibStreamMac;
        private long m_ibCur;

        public DebugStream()
        {
            m_ibStreamMac = m_ibCur = 0;
            m_rgbData = new byte[1024];
            m_cbStream = 1024;
        }

        void EnsureBufferSize(long ibStart, long cbWrite, bool fInit)
        {
            if (m_cbStream < ibStart + cbWrite)
            {
                byte[] rgbSav = m_rgbData;
                long cbNew = ibStart + cbWrite + 4096;
                m_rgbData = new byte[cbNew];

                m_cbStream = cbNew;
                rgbSav.CopyTo(m_rgbData, 0);
            }

            if (m_ibStreamMac < ibStart)
            {
                if (fInit)
                {
                    while (m_ibStreamMac <= ibStart)
                    {
                        m_rgbData[m_ibStreamMac] = 0;
                        m_ibStreamMac++;
                    }
                }
                else
                {
                    m_ibStreamMac = ibStart;
                }
            }
        }

        /*----------------------------------------------------------------------------
        	%%Function: StmCreateFromString
        	%%Qualified: TCore.Debug.DebugStream.StmCreateFromString
        	%%Contact: rlittle
        	
            it turns out, writing a string to a file in .net isn't as simple as you 
            would think. The strings are unicode, and you were probably thinking
            about just plain ascii. so, this will create a stream the way you think
            it would (converting the ascii to bytes, and encoding \n as CRLF
            (0x0d0x0a)
        ----------------------------------------------------------------------------*/
        public static DebugStream StmCreateFromString(string s)
        {
            DebugStream stm = new DebugStream();

            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\n':
                        stm.Write(new byte[] { 0x0d, 0x0a }, 0, 2);
                        break;
                    case (char)0x240d:
                        stm.Write(new byte[] { 0x0d }, 0, 1);
                        break;
                    case (char)0x240a:
                        stm.Write(new byte[] { 0x0a }, 0, 1);
                        break;
                    default:
                        stm.WriteByte((byte)ch);
                        break;
                }
            }

            return stm;
        }

        public static DebugStream StmUtf8CreateFromString(string s)
        {
            DebugStream stm = new DebugStream();

            for (int ich = 0; ich < s.Length; ich++)
            {
                char ch = s[ich];

                switch (ch)
                {
                    case '\n':
                        stm.Write(new byte[] { 0x0d, 0x0a }, 0, 2);
                        break;
                    case (char)0x240d:
                        stm.Write(new byte[] { 0x0d }, 0, 1);
                        break;
                    case (char)0x240a:
                        stm.Write(new byte[] { 0x0a }, 0, 1);
                        break;
                    default:
                        byte[] rgb = System.Text.Encoding.Unicode.GetBytes(s.Substring(ich, 1));
                        stm.WriteByte((byte)ch);
                        break;
                }
            }

            return stm;
        }

        public void WriteTestDataAt(long ibTestStart, byte[] rgbWrite)
        {
            int cbWrite = rgbWrite?.Length ?? 0;

            EnsureBufferSize(ibTestStart, cbWrite, true);
            Seek(ibTestStart, SeekOrigin.Begin);
            if (rgbWrite != null)
                Write(rgbWrite, 0, cbWrite);
        }

        public override bool CanRead => throw new NotImplementedException();
        public override bool CanSeek => throw new NotImplementedException();
        public override bool CanWrite => throw new NotImplementedException();
        public override long Length => m_ibStreamMac;
        public override long Position
        {
            get { return m_ibCur; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    m_ibCur = offset;
                    return m_ibCur;
                case SeekOrigin.Current:
                    m_ibCur = Math.Min(m_ibCur + offset, m_ibStreamMac);
                    return m_ibCur;
                case SeekOrigin.End:
                    m_ibCur = Math.Min(m_ibStreamMac + offset, m_ibStreamMac);
                    return m_ibCur;
            }

            throw new InvalidDataException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (m_ibCur >= m_ibStreamMac)
                return 0;

            long lcbToRead = Math.Min(count, m_ibStreamMac - m_ibCur);
            int cbToRead = (int)lcbToRead;

            if (cbToRead != lcbToRead)
                throw new OverflowException();

            if (cbToRead > 0)
            {
                Array.Copy(m_rgbData, m_ibCur, buffer, offset, cbToRead);
                m_ibCur += cbToRead;
            }

            return cbToRead;
        }

        /*----------------------------------------------------------------------------
            %%Function: SetLength
            %%Qualified: TCore.ListenAz.Stage2.ListenSyncFile.Buffer.DebugStream.SetLength
            %%Contact: rlittle
    
            Set ibStreamMac (filling uninit data to 0 if we are stretching)
    
            this won't shrink the allocated memory though
        ----------------------------------------------------------------------------*/
        public override void SetLength(long value)
        {
            EnsureBufferSize(value, 0, true);

            m_ibStreamMac = value;
        }

        void EnsureMacAdjusted(int count)
        {
            while (m_ibStreamMac < m_ibCur + count)
            {
                m_rgbData[m_ibStreamMac++] = 0;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureBufferSize(m_ibCur, count, true);
            EnsureMacAdjusted(count);

            Array.Copy(buffer, offset, m_rgbData, m_ibCur, count);

            m_ibCur += count;
        }

        static DebugStream StmInit(string sInit)
        {
            DebugStream stm = new DebugStream();
            stm.Write(Encoding.UTF8.GetBytes(sInit), 0, sInit.Length);

            return stm;
        }
    }
}