using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Common.Crypto;

public class Md5StringConverter
{
    public static string ComputeMd5(string input)
    {
        var bytes = Encoding.ASCII.GetBytes(input);
        var hash = MD5.Create().ComputeHash(bytes);

        return string.Join("", hash.Select(b => b.ToString("x2")));
    }
}
