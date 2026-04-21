using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace Upsanctionscreener.Classess
{
    public static class Cryptor
    {

 
            public static string Encrypt(string toEncrypt, bool useHashing)
            {
                byte[] keyArray;
                byte[] toEncryptArray = Encoding.UTF8.GetBytes(toEncrypt);

                // Hardcoded key to match your Decrypt function
                string key = "UpSL!@pAyCoL?&gt;+`";

                // 1. Handle Hashing
                if (useHashing)
                {
                    using (MD5 hashmd5 = MD5.Create())
                    {
                        keyArray = hashmd5.ComputeHash(Encoding.UTF8.GetBytes(key));
                    }
                }
                else
                {
                    keyArray = Encoding.UTF8.GetBytes(key);
                }

                // 2. Handle Encryption
                using (TripleDES tdes = TripleDES.Create())
                {
                    tdes.Key = keyArray;
                    tdes.Mode = CipherMode.ECB; // Must match Decrypt
                    tdes.Padding = PaddingMode.PKCS7;

                    using (ICryptoTransform cTransform = tdes.CreateEncryptor())
                    {
                        byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
                        return Convert.ToBase64String(resultArray, 0, resultArray.Length);
                    }
                }
            }
       







    public static string Decrypt(string cipherString, bool useHashing)
            {
                byte[] keyArray;
                byte[] toEncryptArray = Convert.FromBase64String(cipherString);

                // Hardcoded key as per your requirement
                string key = "UpSL!@pAyCoL?&gt;+`";

                if (useHashing)
                {
                    using (var hashmd5 = MD5.Create())
                    {
                        keyArray = hashmd5.ComputeHash(Encoding.UTF8.GetBytes(key));
                    }
                }
                else
                {
                    keyArray = Encoding.UTF8.GetBytes(key);
                }

                using (var tdes = TripleDES.Create())
                {
                    tdes.Key = keyArray;
                    tdes.Mode = CipherMode.ECB;
                    tdes.Padding = PaddingMode.PKCS7;

                    using (var cTransform = tdes.CreateDecryptor())
                    {
                        byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
                        return Encoding.UTF8.GetString(resultArray);
                    }
                }
            }
        







}
}
