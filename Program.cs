﻿using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using IniParser.Model;
using IniParser;

namespace twitas
{
    class Program
    {
        public static OAuth oAuth { get; private set; }
        public static string ini_file = "twitas.ini";
        public static string twit_file = "twitas.tweet";

        static void Main(string[] args)
        {
            switch (args.Length)
            {
                case 0:
                    break;
                case 1:
                    twit_file = args[0];
                    break;
                case 2:
                    twit_file = args[0];
                    ini_file = args[1];
                    break;
                default:
                    Console.WriteLine("Usage : {0} [[twit_file] [ini_file]]", System.AppDomain.CurrentDomain.FriendlyName);
                    return;
            }

            Log.Init();
            var setting = new INIParser(ini_file);
            var AuthData = "Authenticate";
            string consumerKey = setting.GetValue(AuthData, "ConsumerKey");
            string consumerSecret = setting.GetValue(AuthData, "CconsumerSecret");
            string accessToken = setting.GetValue(AuthData, "AccessToken");
            string accessSecret = setting.GetValue(AuthData, "AccessSecret");
            if (string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(consumerSecret))
            {
                Log.Error("Program", "Unable to get consumerKey / Secret. Please check config file.");
                if (string.IsNullOrWhiteSpace(consumerKey)) { setting.SetValue(AuthData, "ConsumerKey", ""); }
                if (string.IsNullOrWhiteSpace(consumerSecret)) { setting.SetValue(AuthData, "CconsumerSecret", ""); }
                if (string.IsNullOrWhiteSpace(accessToken)) { setting.SetValue(AuthData, "AccessToken", ""); }
                if (string.IsNullOrWhiteSpace(accessSecret)) { setting.SetValue(AuthData, "AccessSecret", ""); }
                setting.Save();
                return;
            }
            else if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(accessSecret))
            {
                oAuth = new OAuth(consumerKey, consumerSecret);
                OAuth.TokenPair tokens = null;
                tokens = oAuth.RequestToken();
                oAuth.User.Token = tokens.Token;
                oAuth.User.Secret = tokens.Token;
                try
                {
                    using (Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = "https://api.twitter.com/oauth/authorize?oauth_token=" + tokens.Token }))
                    { }
                }
                catch
                { }

                string verifier;
                int i;

                do
                {
                    Console.Write("Please input verifier code : ");
                    verifier = Console.ReadLine();
                } while (!int.TryParse(verifier, out i));

                tokens = oAuth.AccessToken(verifier);
                
                if (tokens != null)
                {
                    oAuth.User.Token = tokens.Token;
                    oAuth.User.Secret = tokens.Token;
                    accessToken = oAuth.User.Token;
                    accessSecret = oAuth.User.Secret;
                    setting.SetValue(AuthData, "AccessToken", tokens.Token);
                    setting.SetValue(AuthData, "AccessSecret", tokens.Secret);
                    setting.Save();
                }

                setting.Save();
            }
            else
            {
                oAuth = new OAuth(consumerKey, consumerSecret, accessToken, accessSecret);
            }

            if (oAuth.User.Token != null)
                Publish(new FileInfo(twit_file));
        }
        
        public static bool Publish(FileInfo file)
        {
            var setting = new INIParser(ini_file);
            string header = setting.GetValue("Tweet", "Header").Replace("\\n", "\n").Replace("\\_", " ");
            string footer = setting.GetValue("Tweet", "Footer").Replace("\\n", "\n").Replace("\\_", " ");
            string hokano = setting.GetValue("Tweet", "Hokano").Replace("\\n", "\n").Replace("\\_", " ");

            string text = "";
            if (!string.IsNullOrWhiteSpace(header)) text += header;
            if (file.Exists)
            {
                using (StreamReader r = new StreamReader(file.FullName))
                {
                    int hoka = 0;
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (text.Length + line.Length + hokano.Length + footer.Length <= 140)
                        {
                            text += line + "\n";
                        }
                        else
                        {
                            hoka++;
                        }
                    }
                    if (hoka > 0)
                    {
                        text += string.Format(hokano, (hoka>10?"9+":hoka.ToString()));
                    }
                }
            }
            else
            {
                text += "Hello World!\n";
            }
            if (!string.IsNullOrWhiteSpace(footer)) text += footer;

            Console.Write("Tweet : " + text);
            object obj = new { status = text };

            try
            {
                var buff = Encoding.UTF8.GetBytes(OAuth.ToString(obj));

                var req = oAuth.MakeRequest("POST", "https://api.twitter.com/1.1/statuses/update.json", obj);
                req.GetRequestStream().Write(buff, 0, buff.Length);

                using (var res = req.GetResponse())
                using (var reader = new StreamReader(res.GetResponseStream()))
                {
                    string str = reader.ReadToEnd();
                    using (StreamWriter w = new StreamWriter(file.FullName))
                    {
                        w.Write(str);
                        Console.WriteLine(str);
                    }
                }
                return true;
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    using (var res = ex.Response)
                    using (var reader = new StreamReader(res.GetResponseStream()))
                    {
                        string str = reader.ReadToEnd();
                        using (StreamWriter w = new StreamWriter(file.FullName))
                        {
                            w.Write(str);
                            Console.WriteLine(str);
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public class OAuth
        {
            public class TokenPair
            {
                public TokenPair()
                {
                }
                public TokenPair(string token, string secret)
                {
                    this.Token = token;
                    this.Secret = secret;
                }
                public string Token { get; set; }
                public string Secret { get; set; }
            }

            static OAuth()
            {
                ServicePointManager.Expect100Continue = false;
            }

            public OAuth(string appToken, string appSecret)
                : this(appToken, appSecret, null, null)
            {
            }
            public OAuth(string appToken, string appSecret, string userToken, string userSecret)
            {
                this.App = new TokenPair(appToken, appSecret);
                this.User = new TokenPair(userToken, userSecret);
            }

            public TokenPair App { get; private set; }
            public TokenPair User { get; private set; }

            private static string[] oauth_array = { "oauth_consumer_key", "oauth_version", "oauth_nonce", "oauth_signature", "oauth_signature_method", "oauth_timestamp", "oauth_token", "oauth_callback" };

            public WebRequest MakeRequest(string method, string url, object data = null)
            {
                method = method.ToUpper();
                var uri = new Uri(url);
                var dic = new SortedDictionary<string, object>();

                if (!string.IsNullOrWhiteSpace(uri.Query))
                    OAuth.AddDictionary(dic, uri.Query);

                if (data != null)
                    OAuth.AddDictionary(dic, data);

                if (!string.IsNullOrWhiteSpace(this.User.Token))
                    dic.Add("oauth_token", UrlEncode(this.User.Token));

                dic.Add("oauth_consumer_key", UrlEncode(this.App.Token));
                dic.Add("oauth_nonce", OAuth.GetNonce());
                dic.Add("oauth_timestamp", OAuth.GetTimeStamp());
                dic.Add("oauth_signature_method", "HMAC-SHA1");
                dic.Add("oauth_version", "1.0");

                var hashKey = string.Format(
                    "{0}&{1}",
                    UrlEncode(this.App.Secret),
                    this.User.Secret == null ? null : UrlEncode(this.User.Secret));
                var hashData = string.Format(
                        "{0}&{1}&{2}",
                        method.ToUpper(),
                        UrlEncode(string.Format("{0}{1}{2}{3}", uri.Scheme, Uri.SchemeDelimiter, uri.Host, uri.AbsolutePath)),
                        UrlEncode(OAuth.ToString(dic)));

                using (var hash = new HMACSHA1(Encoding.UTF8.GetBytes(hashKey)))
                    dic.Add("oauth_signature", UrlEncode(Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(hashData)))));

                var sbData = new StringBuilder();
                sbData.Append("OAuth ");
                foreach (var st in dic)
                    if (Array.IndexOf<string>(oauth_array, st.Key) >= 0)
                        sbData.AppendFormat("{0}=\"{1}\",", st.Key, Convert.ToString(st.Value));
                sbData.Remove(sbData.Length - 1, 1);

                var str = sbData.ToString();

                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Method = method;
                req.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
                req.UserAgent = "twitas/1.0 (.NET 4.5)";
                req.Headers.Add("Authorization", sbData.ToString());

                if (method == "POST")
                    req.ContentType = "application/x-www-form-urlencoded";

                return req;
            }

            private static string GetNonce()
            {
                return Guid.NewGuid().ToString("N");
            }

            private static DateTime GenerateTimeStampDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            private static string GetTimeStamp()
            {
                return Convert.ToInt64((DateTime.UtcNow - GenerateTimeStampDateTime).TotalSeconds).ToString();
            }

            private const string unreservedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~";
            private static string UrlEncode(string str)
            {
                var uriData = Uri.EscapeDataString(str);
                var sb = new StringBuilder(uriData.Length);

                for (int i = 0; i < uriData.Length; ++i)
                {
                    switch (uriData[i])
                    {
                        case '!': sb.Append("%21"); break;
                        case '*': sb.Append("%2A"); break;
                        case '\'': sb.Append("%5C"); break;
                        case '(': sb.Append("%28"); break;
                        case ')': sb.Append("%29"); break;
                        default: sb.Append(uriData[i]); break;
                    }
                }

                return sb.ToString();
            }

            private static string ToString(IDictionary<string, object> dic)
            {
                if (dic == null) return null;

                var sb = new StringBuilder();

                if (dic.Count > 0)
                {
                    foreach (var st in dic)
                        if (st.Value is bool)
                            sb.AppendFormat("{0}={1}&", st.Key, (bool)st.Value ? "true" : "false");
                        else
                            sb.AppendFormat("{0}={1}&", st.Key, Convert.ToString(st.Value));

                    if (sb.Length > 0)
                        sb.Remove(sb.Length - 1, 1);
                }

                return sb.ToString();
            }

            public static string ToString(object values)
            {
                if (values == null) return null;

                var sb = new StringBuilder();

                string name;
                object value;

                foreach (var p in values.GetType().GetProperties())
                {
                    if (!p.CanRead) continue;

                    name = p.Name;
                    value = p.GetValue(values, null);

                    if (value is bool)
                        sb.AppendFormat("{0}={1}&", name, (bool)value ? "true" : "false");
                    else
                        sb.AppendFormat("{0}={1}&", name, UrlEncode(Convert.ToString(value)));
                }

                if (sb.Length > 0)
                    sb.Remove(sb.Length - 1, 1);

                return sb.ToString();
            }

            private static void AddDictionary(IDictionary<string, object> dic, string query)
            {
                if (!string.IsNullOrWhiteSpace(query) || (query.Length > 1))
                {
                    int read = 0;
                    int find = 0;

                    if (query[0] == '?')
                        read = 1;

                    string key, val;

                    while (read < query.Length)
                    {
                        find = query.IndexOf('=', read);
                        key = query.Substring(read, find - read);
                        read = find + 1;

                        find = query.IndexOf('&', read);
                        if (find > 0)
                        {
                            if (find - read == 1)
                                val = null;
                            else
                                val = query.Substring(read, find - read);

                            read = find + 1;
                        }
                        else
                        {
                            val = query.Substring(read);

                            read = query.Length;
                        }

                        dic[key] = val;
                    }
                }
            }

            private static void AddDictionary(IDictionary<string, object> dic, object values)
            {
                object value;

                foreach (var p in values.GetType().GetProperties())
                {
                    if (!p.CanRead) continue;
                    value = p.GetValue(values, null);

                    if (value is bool)
                        dic[p.Name] = (bool)value ? "true" : "false";
                    else
                        dic[p.Name] = UrlEncode(Convert.ToString(value));


                }
            }

            public TokenPair RequestToken()
            {
                try
                {
                    var req = MakeRequest("POST", "https://api.twitter.com/oauth/request_token");
                    using (var res = req.GetResponse())
                    using (var reader = new StreamReader(res.GetResponseStream()))
                    {
                        var str = reader.ReadToEnd();

                        var token = new TokenPair();
                        token.Token = Regex.Match(str, @"oauth_token=([^&]+)").Groups[1].Value;
                        token.Secret = Regex.Match(str, @"oauth_token_secret=([^&]+)").Groups[1].Value;

                        return token;
                    }
                }
                catch
                {
                    return null;
                }
            }

            public TokenPair AccessToken(string verifier)
            {
                try
                {
                    var obj = new { oauth_verifier = verifier };
                    var buff = Encoding.UTF8.GetBytes(OAuth.ToString(obj));

                    var req = MakeRequest("POST", "https://api.twitter.com/oauth/access_token", obj);
                    req.GetRequestStream().Write(buff, 0, buff.Length);

                    using (var res = req.GetResponse())
                    using (var reader = new StreamReader(res.GetResponseStream()))
                    {
                        var str = reader.ReadToEnd();

                        var token = new TokenPair();
                        token.Token = Regex.Match(str, @"oauth_token=([^&]+)").Groups[1].Value;
                        token.Secret = Regex.Match(str, @"oauth_token_secret=([^&]+)").Groups[1].Value;

                        return token;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    public class Log
    {
        public static bool IsInited { get; private set; }
        public static bool LogTrace { get; private set; }

        public static void Init(bool LogTrace = false)
        {
            try
            {
                IsInited = true;
                Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
                Trace.AutoFlush = true;
            }
            catch
            {
                IsInited = false;
            }
        }

        public static void Print(string tag, string message)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Output(tag, message);
        }
        public static void Http(string tag, string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Output(tag, message);
        }
        public static void Debug(string tag, string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Output(tag, message);
        }
        public static void Warning(string tag, string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Output(tag, message);
        }
        public static void Error(string tag, string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Output(tag, message);
        }

        private static void Output(string tag, string message)
        {
            string log = string.Format("{0} : {1}", tag, message);
            if (IsInited)
            {
                Trace.WriteLine(log);
            }
            else
            {
                Console.WriteLine(log);
            }
        }

        public static void StackTrace()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            if (IsInited)
            {
                Trace.Write(new StackTrace(true));
            }
            else
            {
                Console.WriteLine(string.Format("Inflate stacktrace() : \n{0}", new StackTrace(true)));
            }
        }

        public static void Indent()
        {
            if (!IsInited) return;
            Trace.Indent();
        }

        public static void Unindent()
        {
            if (!IsInited) return;
            Trace.Unindent();
        }
    }

    public class INIParser
    {
        private string iniPath;
        private IniData data;

        public INIParser(string path)
        {
            this.iniPath = path;
            if (File.Exists(iniPath))
            {
                data = new FileIniDataParser().ReadFile(iniPath);
                string @out = string.Format("Read INI - [{0}]", iniPath);
                foreach (var section in data.Sections)
                {
                    @out += string.Format("\n    [{0}]", section.SectionName);
                    foreach (var key in section.Keys)
                    {
                        @out += string.Format("\n        {0} = {1}", key.KeyName, data[section.SectionName][key.KeyName]);
                    }
                }
                Log.Debug("INIParser", @out);
            }
            else
            {
                Log.Error("INIParser", string.Format("[{0}] is not correct directory. new inidata generated.", iniPath));
                data = new IniData();
            }
        }

        #region WINApi

        //[DllImport( "kernel32.dll" )]
        //private static extern int GetPrivateProfileString(
        //	String section,
        //	String key,
        //	String def,
        //	StringBuilder retVal,
        //	int size,
        //	String filePath );

        //[DllImport( "kernel32.dll" )]
        //private static extern long WritePrivateProfileString(
        //	String section,
        //	String key,
        //	String val,
        //	String filePath );

        //public String GetValue( String Section, String Key )
        //{
        //	StringBuilder temp = new StringBuilder(255);
        //	int i = GetPrivateProfileString(Section, Key, "", temp, 255, iniPath);
        //	return temp.ToString( );
        //}

        //public void SetValue( String Section, String Key, String Value )
        //{
        //	WritePrivateProfileString( Section, Key, Value, iniPath );
        //}

        #endregion

        public string GetValue(string Section, string Key)
        {
            try
            {
                return data[Section][Key];
            }
            catch
            {
                return null;
            }
        }

        public void SetValue(string Section, string Key, object Value)
        {
            if (!data.Sections.ContainsSection(Section)) data.Sections.AddSection(Section);
            if (!data[Section].ContainsKey(Key)) data[Section].AddKey(Key);

            data[Section][Key] = Value.ToString();
        }

        internal void Save()
        {
            new FileIniDataParser().WriteFile(iniPath, data, Encoding.UTF8);
        }

        #region FileIniDataParser



        #endregion
    }
}
