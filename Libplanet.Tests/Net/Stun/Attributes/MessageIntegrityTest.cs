using Libplanet.Net.Stun.Attributes;
using Xunit;

namespace Libplanet.Tests.Net.Stun.Attributes
{
    public class MessageIntegrityTest
    {
        [Fact]
        public void EncodeToBytes()
        {
            byte[] digest = new byte[]
            {
                0xfd, 0x49, 0x5a, 0x92, 0xb5, 0x9f, 0x59, 0x67, 0x33, 0xce,
                0xcf, 0xf4, 0x45, 0xb7, 0xa5, 0x88, 0x04, 0x8a, 0x39, 0x05,
            };
            var attr = new MessageIntegrity(digest);

            Assert.Equal(
                new byte[]
                {
                    0x00, 0x08, 0x00, 0x14,
                    0xfd, 0x49, 0x5a, 0x92, 0xb5, 0x9f, 0x59, 0x67, 0x33, 0xce,
                    0xcf, 0xf4, 0x45, 0xb7, 0xa5, 0x88, 0x04, 0x8a, 0x39, 0x05,
                },
                attr.ToByteArray());
        }

        [Fact]
        public void CalculateProperly()
        {
            var username = "ae0633cd58ba097a1167c6d2cc4e236db52256a40d565f11edf76c02d13db93c";
            var password = "U/X7qP2MosBElz8/T2CESYkvqyUIOoGTlOJoSsHvZkQ=";
            var realm = "twilio.com";
            var message = new byte[]
            {
                0x00, 0x03, 0x00, 0x90, 0x21, 0x12, 0xa4, 0x42, 0xf5, 0xdb,
                0xe7, 0xc1, 0x2a, 0x74, 0xbe, 0xf9, 0x8b, 0x16, 0x56, 0x3e,
                0x00, 0x19, 0x00, 0x04, 0x06, 0x00, 0x00, 0x00, 0x00, 0x0d,
                0x00, 0x04, 0x00, 0x00, 0x03, 0x09, 0x00, 0x06, 0x00, 0x40,
                0x61, 0x65, 0x30, 0x36, 0x33, 0x33, 0x63, 0x64, 0x35, 0x38,
                0x62, 0x61, 0x30, 0x39, 0x37, 0x61, 0x31, 0x31, 0x36, 0x37,
                0x63, 0x36, 0x64, 0x32, 0x63, 0x63, 0x34, 0x65, 0x32, 0x33,
                0x36, 0x64, 0x62, 0x35, 0x32, 0x32, 0x35, 0x36, 0x61, 0x34,
                0x30, 0x64, 0x35, 0x36, 0x35, 0x66, 0x31, 0x31, 0x65, 0x64,
                0x66, 0x37, 0x36, 0x63, 0x30, 0x32, 0x64, 0x31, 0x33, 0x64,
                0x62, 0x39, 0x33, 0x63, 0x00, 0x15, 0x00, 0x10, 0x37, 0x35,
                0x64, 0x34, 0x35, 0x34, 0x31, 0x39, 0x63, 0x33, 0x39, 0x33,
                0x34, 0x33, 0x66, 0x65, 0x00, 0x14, 0x00, 0x0a, 0x74, 0x77,
                0x69, 0x6c, 0x69, 0x6f, 0x2e, 0x63, 0x6f, 0x6d, 0x00, 0x00,
            };

            MessageIntegrity attr =
                MessageIntegrity.Calculate(username, password, realm, message);

            Assert.Equal(
                new byte[]
                {
                    0x77, 0xe8, 0xcf, 0x30, 0x9e, 0x85, 0x6c, 0x22, 0x72, 0x53,
                    0xa3, 0xb7, 0xe0, 0x35, 0x7c, 0xc2, 0x30, 0xfc, 0xbc, 0xf4,
                },
                attr.Value
            );
        }
    }
}
