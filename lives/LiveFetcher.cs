// LiveConfig - 直播源便携获取器 (TVBox/FongMi LiveConfig 生成)
// 来源: qmtv 配置节点 + 咕噜APP + 澜露央卫订阅 + IPTV4 公益源
// 分组顺序: 咕噜· 最前 -> 常规/IPTV4 中间 -> 澜露· 最后
// 编译: net48, 需引用 System.Web.Extensions
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal class Program
{
	private class Group
	{
		public string Name;

		public Dictionary<string, List<string>> Chs = new Dictionary<string, List<string>>();
	}

	private const string ZIP_PASSWORD = "DBhkhdnefkhfq,#%";

	private const string DEFAULT_EPG = "http://207.56.16.135:9999/tv/diyp.php?ch={name}&date={date}";

	private const string LANLU_URL = "http://txt.gt.tc/users/weidi12345678.txt";

	private const string LANLU_LOCAL = "weidi12345678.txt";

	// IPTV4 公益源: TXT 与 M3U 为同一源的不同格式, 只取 TXT 一份即可
	private static readonly string[] IPTV4_URLS = new string[2]
	{
		"https://live.zbds.top/tv/iptv4.txt",
		"https://gh-proxy.com/raw.githubusercontent.com/vbskycn/iptv/refs/heads/master/tv/iptv4.txt"
	};

	private static readonly string[] CONFIG_NODES = new string[2] { "http://207.56.13.146:9999/qmtv/config.json", "http://qmtv.tvbus.cc/qmtv/config.json" };

	private static readonly string[] AD_KEYWORDS = new string[14]
	{
		"applive", "甄选", "好物", "福利", "会员专享", "今日推荐", "精品推荐", "优享", "养生馆", "严选",
		"银龄", "美好生活", "健康有约", "健康在线"
	};

	private static readonly string[] BLOCK_GROUPS = new string[1] { "彩虹" };

	private static string zipPass = ZIP_PASSWORD;

	private static List<string> adKw = new List<string>(AD_KEYWORDS);

	private static int filteredCount = 0;

	private static List<Group> groups = new List<Group>();

	private static uint[] CRCT = BuildCrcTable();

	private static int Main(string[] args)
	{
		Console.WriteLine("========================================================");
		Console.WriteLine("   直播源便携获取器 (TVBox/FongMi LiveConfig 生成)");
		Console.WriteLine("   来源: qmtv + 咕噜 + 澜露 + IPTV4 | 顺序: 咕噜最前 澜露最后");
		Console.WriteLine("   双击运行: 自动获取最新配置 -> 下载ZIP -> 生成JSON");
		Console.WriteLine("========================================================");
		string target = null;
		string outFile = null;
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (arg == "-p" && i + 1 < args.Length)
			{
				zipPass = args[++i];
			}
			else if (arg == "-kw" && i + 1 < args.Length)
			{
				adKw = (from s in args[++i].Split(new char[3] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries)
					select s.Trim().ToLowerInvariant() into s
					where s.Length > 0
					select s).ToList();
			}
			else if (arg == "-o" && i + 1 < args.Length)
			{
				outFile = args[++i];
			}
			else
			{
				target = arg;
			}
		}
		if (outFile == null)
		{
			outFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sss.json");
		}
		ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
		try
		{
			Run(target, outFile);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[失败] " + ex.Message);
		}
		Console.Write("\n按回车键退出...");
		Console.ReadLine();
		return 0;
	}

	private static void Run(string target, string outFile)
	{
		string epg = DEFAULT_EPG;
		byte[] input = null;
		if (target != null && File.Exists(target))
		{
			Console.WriteLine("[本地] 加载文件: " + target);
			input = File.ReadAllBytes(target);
		}
		else if (target != null && Regex.IsMatch(target, "^https?://", RegexOptions.IgnoreCase))
		{
			Console.WriteLine("[下载] " + target);
			input = HttpGetBytes(target);
		}
		else
		{
			foreach (string node in CONFIG_NODES)
			{
				Console.WriteLine("[节点] " + node);
				string cfgText;
				try
				{
					cfgText = Encoding.UTF8.GetString(HttpGetBytes(node));
				}
				catch (Exception ex)
				{
					Console.WriteLine("       节点失败: " + ex.Message);
					continue;
				}
				Match mEpg = Regex.Match(cfgText, "\"epg\"\\s*:\\s*\"([^\"]+)\"");
				if (mEpg.Success)
				{
					epg = JsonUnescape(mEpg.Groups[1].Value);
				}
				Match mApk = Regex.Match(cfgText, "\"apk\"\\s*:\\s*\"([^\"]+)\"");
				if (mApk.Success)
				{
					Console.WriteLine("       APK更新地址: " + JsonUnescape(mApk.Groups[1].Value));
				}
				Match mSrc = Regex.Match(cfgText, "\"source\"\\s*:\\s*\"([^\"]+)\"");
				if (!mSrc.Success)
				{
					Console.WriteLine("       返回中无 source 字段");
					continue;
				}
				string srcUrl = JsonUnescape(mSrc.Groups[1].Value);
				Console.WriteLine("       最新源: " + srcUrl);
				try
				{
					input = HttpGetBytes(srcUrl);
				}
				catch (Exception ex2)
				{
					Console.WriteLine("       下载失败: " + ex2.Message);
					continue;
				}
				break;
			}
			if (input == null)
			{
				throw new Exception("所有配置节点均不可用, 可将 ZIP/TXT 拖到本 EXE 上离线解析");
			}
		}
		List<KeyValuePair<string, byte[]>> files = new List<KeyValuePair<string, byte[]>>();
		if (input.Length > 4 && input[0] == 80 && input[1] == 75)
		{
			Console.WriteLine("[ZIP] 检测到压缩包, 开始解密解压 (密码: " + zipPass + ")");
			files = ReadZip(input);
			Console.WriteLine("[ZIP] 解出 " + files.Count + " 个文件: " + string.Join(", ", files.Select((KeyValuePair<string, byte[]> f) => f.Key).ToArray()));
		}
		else
		{
			files.Add(new KeyValuePair<string, byte[]>("input.txt", input));
		}
		foreach (KeyValuePair<string, byte[]> item in files)
		{
			string content = DecodeBytes(item.Value);
			if (content.TrimStart().StartsWith("[") || content.TrimStart().StartsWith("{"))
			{
				IngestJson(content, item.Key);
			}
			else if (content.TrimStart().StartsWith("#EXTM3U"))
			{
				IngestM3u(content, item.Key);
			}
			else
			{
				IngestTxt(content, item.Key);
			}
		}
		// 深拷贝快照: 之后各追加源可能合流进常规分组, 保证 sss_qimi.json 仍为纯 qmtv
		List<Group> gsQmtv = CloneGroups(groups);
		try
		{
			FetchGulu();
		}
		catch (Exception ex3)
		{
			Console.WriteLine("[咕噜] 获取失败(不影响主流程): " + ex3.Message);
		}
		try
		{
			FetchLanlu();
		}
		catch (Exception ex4)
		{
			Console.WriteLine("[澜露] 获取失败(不影响主流程): " + ex4.Message);
		}
		try
		{
			FetchIptv4();
		}
		catch (Exception ex5)
		{
			Console.WriteLine("[IPTV4] 获取失败(不影响主流程): " + ex5.Message);
		}
		if (groups.Count == 0)
		{
			throw new Exception("没有解析到任何频道");
		}
		// 分组排序: 咕噜· 最前, 澜露· 最后, 其余按原顺序居中
		groups = OrderGroups(groups);
		string contents = BuildJson(groups, epg);
		string outDir = Path.GetDirectoryName(outFile);
		if (string.IsNullOrEmpty(outDir))
		{
			outDir = AppDomain.CurrentDomain.BaseDirectory;
		}
		string baseName = Path.GetFileNameWithoutExtension(outFile);
		string ext = Path.GetExtension(outFile);
		if (string.IsNullOrEmpty(ext))
		{
			ext = ".json";
		}
		string qimiFile = Path.Combine(outDir, baseName + "_qimi" + ext);
		string guluFile = Path.Combine(outDir, baseName + "_gulu" + ext);
		string lanluFile = Path.Combine(outDir, baseName + "_lanlu" + ext);
		File.WriteAllText(outFile, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.WriteAllText(qimiFile, BuildJson(gsQmtv, epg), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.WriteAllText(guluFile, BuildJson(groups.Where((Group g) => g.Name.StartsWith("咕噜·")).ToList(), epg), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.WriteAllText(lanluFile, BuildJson(groups.Where((Group g) => g.Name.StartsWith("澜露·")).ToList(), epg), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		int totalGroups = groups.Count;
		int totalChs = groups.Sum((Group g) => g.Chs.Count);
		int totalUrls = groups.Sum((Group g) => g.Chs.Values.Sum((List<string> v) => v.Count));
		int qmtvG = 0;
		int qmtvC = 0;
		int guluG = 0;
		int guluC = 0;
		int lanluG = 0;
		int lanluC = 0;
		foreach (Group group in groups)
		{
			if (group.Name.StartsWith("咕噜·"))
			{
				guluG++;
				guluC += group.Chs.Count;
			}
			else if (group.Name.StartsWith("澜露·"))
			{
				lanluG++;
				lanluC += group.Chs.Count;
			}
			else
			{
				qmtvG++;
				qmtvC += group.Chs.Count;
			}
		}
		Console.WriteLine("\n--------------------------------------------------------");
		Console.WriteLine("总计 分组 " + totalGroups + " | 频道 " + totalChs + " | 线路 " + totalUrls + " | 已过滤广告 " + filteredCount);
		Console.WriteLine("常规+IPTV4 来源: 分组 " + qmtvG + " | 频道 " + qmtvC + "    咕噜来源: 分组 " + guluG + " | 频道 " + guluC + "    澜露来源: 分组 " + lanluG + " | 频道 " + lanluC);
		Console.WriteLine("分组顺序: 咕噜· 最前 -> 常规/IPTV4 -> 澜露· 最后");
		Console.WriteLine("EPG: " + epg);
		Console.WriteLine("已生成: " + outFile + "  (合并)");
		Console.WriteLine("已生成: " + qimiFile + "  (仅 qmtv)");
		Console.WriteLine("已生成: " + guluFile + "  (仅咕噜)");
		Console.WriteLine("已生成: " + lanluFile + "  (仅澜露)");
		Console.WriteLine("在 FongMi TV / 影视仓 中以本 JSON 文件作为直播配置导入即可");
		Console.WriteLine("--------------------------------------------------------");
	}

	// 分组排序: 咕噜· 最前, 澜露· 最后, 其余保持原相对顺序居中
	private static List<Group> OrderGroups(List<Group> src)
	{
		List<Group> result = new List<Group>();
		result.AddRange(src.Where((Group g) => g.Name.StartsWith("咕噜·")));
		result.AddRange(src.Where((Group g) => !g.Name.StartsWith("咕噜·") && !g.Name.StartsWith("澜露·")));
		result.AddRange(src.Where((Group g) => g.Name.StartsWith("澜露·")));
		return result;
	}

	// 深拷贝分组快照
	private static List<Group> CloneGroups(List<Group> src)
	{
		List<Group> result = new List<Group>();
		foreach (Group g in src)
		{
			Group n = new Group();
			n.Name = g.Name;
			foreach (KeyValuePair<string, List<string>> kv in g.Chs)
			{
				n.Chs[kv.Key] = new List<string>(kv.Value);
			}
			result.Add(n);
		}
		return result;
	}

	// IPTV4 公益源: TXT 与 M3U 内容相同, 只取 TXT; 不加前缀, 与常规同名分组/频道自动合流
	private static void FetchIptv4()
	{
		byte[] data = null;
		foreach (string url in IPTV4_URLS)
		{
			Console.WriteLine("\n[IPTV4] 拉取 iptv4 直播源(TXT): " + url);
			try
			{
				data = HttpGetBytes(url);
				break;
			}
			catch (Exception ex)
			{
				Console.WriteLine("[IPTV4] 下载失败: " + ex.Message);
			}
		}
		if (data == null)
		{
			throw new Exception("所有 IPTV4 地址均不可用");
		}
		int before = groups.Sum((Group g) => g.Chs.Count);
		IngestTxt(DecodeBytes(data), "IPTV4", null);
		int after = groups.Sum((Group g) => g.Chs.Count);
		Console.WriteLine("[IPTV4] 入库完成: 频道 " + before + " -> " + after + " (无前缀, 同组同名频道自动合并线路)");
	}

	private static byte[] HttpGetBytes(string url)
	{
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
		req.UserAgent = "okhttp/3.12.0";
		req.Timeout = 20000;
		req.ReadWriteTimeout = 30000;
		req.AllowAutoRedirect = true;
		using HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
		using MemoryStream ms = new MemoryStream();
		using Stream stream = resp.GetResponseStream();
		byte[] buf = new byte[65536];
		int count;
		while ((count = stream.Read(buf, 0, buf.Length)) > 0)
		{
			ms.Write(buf, 0, count);
		}
		Console.WriteLine("       下载成功: " + ms.Length + " 字节");
		return ms.ToArray();
	}

	private static string DecodeBytes(byte[] b)
	{
		try
		{
			return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(b);
		}
		catch
		{
			return Encoding.GetEncoding("gbk").GetString(b);
		}
	}

	private static string JsonUnescape(string s)
	{
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] != '\\' || i + 1 >= s.Length)
			{
				sb.Append(s[i]);
				continue;
			}
			char c = s[++i];
			switch (c)
			{
			case '/':
				sb.Append('/');
				break;
			case '"':
				sb.Append('"');
				break;
			case '\\':
				sb.Append('\\');
				break;
			case 'n':
				sb.Append('\n');
				break;
			case 'r':
				sb.Append('\r');
				break;
			case 't':
				sb.Append('\t');
				break;
			case 'u':
			{
				if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, null, out var result))
				{
					sb.Append((char)result);
					i += 4;
				}
				else
				{
					sb.Append(c);
				}
				break;
			}
			default:
				sb.Append(c);
				break;
			}
		}
		return sb.ToString();
	}

	private static List<KeyValuePair<string, byte[]>> ReadZip(byte[] buf)
	{
		int eocd = -1;
		for (int i = buf.Length - 22; i >= Math.Max(0, buf.Length - 65557); i--)
		{
			if (buf[i] == 80 && buf[i + 1] == 75 && buf[i + 2] == 5 && buf[i + 3] == 6)
			{
				eocd = i;
				break;
			}
		}
		if (eocd < 0)
		{
			throw new Exception("ZIP 目录未找到");
		}
		int entryCount = BitConverter.ToUInt16(buf, eocd + 10);
		int off = (int)BitConverter.ToUInt32(buf, eocd + 16);
		byte[] pw = Encoding.UTF8.GetBytes(zipPass);
		List<KeyValuePair<string, byte[]>> result = new List<KeyValuePair<string, byte[]>>();
		for (int n = 0; n < entryCount && off + 46 <= buf.Length && BitConverter.ToUInt32(buf, off) == 33639248; n++)
		{
			int flags = BitConverter.ToUInt16(buf, off + 8);
			int method = BitConverter.ToUInt16(buf, off + 10);
			int chk = BitConverter.ToUInt16(buf, off + 12);
			uint crc = BitConverter.ToUInt32(buf, off + 16);
			int csize = (int)BitConverter.ToUInt32(buf, off + 20);
			int nameLen = BitConverter.ToUInt16(buf, off + 28);
			int extraLen = BitConverter.ToUInt16(buf, off + 30);
			int cmtLen = BitConverter.ToUInt16(buf, off + 32);
			int lho = (int)BitConverter.ToUInt32(buf, off + 42);
			string name = Encoding.UTF8.GetString(buf, off + 46, nameLen);
			string aesBits = null;
			if (method == 99)
			{
				bool found = false;
				int elen;
				for (int j = off + 30 + nameLen; j + 4 <= off + 30 + nameLen + extraLen; j += 4 + elen)
				{
					int eId = BitConverter.ToUInt16(buf, j);
					elen = BitConverter.ToUInt16(buf, j + 2);
					if (eId == 39169)
					{
						method = BitConverter.ToUInt16(buf, j + 9);
						aesBits = "aes:" + buf[j + 8];
						found = true;
						break;
					}
				}
				if (!found)
				{
					off += 46 + nameLen + extraLen + cmtLen;
					continue;
				}
			}
			else if ((flags & 1) != 0)
			{
				aesBits = "zip";
			}
			if (!name.EndsWith("/") && csize > 0)
			{
				int dataOff = lho + 30 + BitConverter.ToUInt16(buf, lho + 26) + BitConverter.ToUInt16(buf, lho + 28);
				byte[] data = Slice(buf, dataOff, csize);
				try
				{
					if (aesBits == "zip")
					{
						data = ZipCryptoDecrypt(data, pw, (chk >> 8) & 0xFF, (int)((crc >> 24) & 0xFF));
					}
					else if (aesBits != null && aesBits.StartsWith("aes"))
					{
						data = AesDecrypt(data, pw, int.Parse(aesBits.Split(':')[1]));
					}
				}
				catch (Exception ex)
				{
					throw new Exception("解密失败(" + name + "): " + ex.Message + " —— 检查解压密码");
				}
				byte[] value;
				switch (method)
				{
				case 0:
					value = data;
					break;
				case 8:
					value = Inflate(data);
					break;
				default:
					off += 46 + nameLen + extraLen + cmtLen;
					continue;
				}
				result.Add(new KeyValuePair<string, byte[]>(name, value));
			}
			off += 46 + nameLen + extraLen + cmtLen;
		}
		return result;
	}

	private static byte[] Slice(byte[] src, int off, int len)
	{
		byte[] dst = new byte[len];
		Buffer.BlockCopy(src, off, dst, 0, len);
		return dst;
	}

	private static byte[] Inflate(byte[] raw)
	{
		using MemoryStream stream = new MemoryStream(raw);
		using DeflateStream ds = new DeflateStream(stream, CompressionMode.Decompress);
		using MemoryStream ms = new MemoryStream();
		ds.CopyTo(ms);
		return ms.ToArray();
	}

	private static uint[] BuildCrcTable()
	{
		uint[] table = new uint[256];
		for (uint i = 0u; i < 256; i++)
		{
			uint c = i;
			for (int k = 0; k < 8; k++)
			{
				c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
			}
			table[i] = c;
		}
		return table;
	}

	private static void ZcUpd(uint[] k, byte b)
	{
		k[0] = (k[0] >> 8) ^ CRCT[(k[0] ^ b) & 0xFF];
		k[1] = k[1] + (k[0] & 0xFF);
		k[1] = k[1] * 134775813 + 1;
		k[2] = (k[2] >> 8) ^ CRCT[(k[2] ^ (byte)(k[1] >> 24)) & 0xFF];
	}

	private static byte[] ZipCryptoDecrypt(byte[] data, byte[] pw, int chk1, int chk2)
	{
		if (pw == null || pw.Length == 0)
		{
			throw new Exception("ZIP 已加密, 请用 -p 提供解压密码");
		}
		uint[] k = new uint[3] { 305419896u, 591751049u, 878082192u };
		foreach (byte b in pw)
		{
			ZcUpd(k, b);
		}
		byte[] dec = new byte[data.Length];
		for (int j = 0; j < data.Length; j++)
		{
			ushort t = (ushort)((k[2] | 2u) & 0xFFFFu);
			byte v = (byte)(data[j] ^ (byte)((uint)(t * (t ^ 1) >> 8) & 0xFFu));
			dec[j] = v;
			ZcUpd(k, v);
		}
		if (dec.Length >= 12 && dec[11] != chk1 && dec[11] != chk2)
		{
			throw new Exception("密码校验不通过");
		}
		byte[] plain = new byte[dec.Length - 12];
		Buffer.BlockCopy(dec, 12, plain, 0, plain.Length);
		return plain;
	}

	private static byte[] AesDecrypt(byte[] data, byte[] pw, int strength)
	{
		int keyLen = strength switch
		{
			2 => 24,
			1 => 16,
			_ => 32,
		};
		int saltLen = strength switch
		{
			2 => 12,
			1 => 8,
			_ => 16,
		};
		if (data.Length < saltLen + 2)
		{
			throw new Exception("数据过短");
		}
		byte[] salt = Slice(data, 0, saltLen);
		byte[] body = Slice(data, saltLen + 2, data.Length - saltLen - 2);
		using Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(pw, salt, 1000, HashAlgorithmName.SHA1);
		byte[] dk = kdf.GetBytes(keyLen + 16 + 2);
		byte[] key = Slice(dk, 0, keyLen);
		Slice(dk, keyLen, 16);
		using Aes aes = Aes.Create();
		aes.Key = key;
		aes.Mode = CipherMode.ECB;
		aes.Padding = PaddingMode.None;
		using ICryptoTransform enc = aes.CreateEncryptor();
		byte[] outBuf = new byte[body.Length];
		byte[] ctr = new byte[16];
		int pos = 0;
		int block = 1;
		while (pos < body.Length)
		{
			uint v = (uint)block;
			ctr[15] = (byte)(v & 0xFFu);
			ctr[14] = (byte)((v >> 8) & 0xFFu);
			ctr[13] = (byte)((v >> 16) & 0xFFu);
			ctr[12] = (byte)((v >> 24) & 0xFFu);
			byte[] ks = enc.TransformFinalBlock(ctr, 0, 16);
			int n = Math.Min(16, body.Length - pos);
			for (int i = 0; i < n; i++)
			{
				outBuf[pos + i] = (byte)(body[pos + i] ^ ks[i]);
			}
			pos += 16;
			block++;
		}
		return outBuf;
	}

	private static void AddCh(string groupName, string chName, List<string> urls)
	{
		string probe = (chName + " " + string.Join(" ", urls.ToArray())).ToLowerInvariant();
		foreach (string kw in adKw)
		{
			if (probe.Contains(kw))
			{
				filteredCount++;
				return;
			}
		}
		foreach (string blocked in BLOCK_GROUPS)
		{
			if (groupName.Contains(blocked))
			{
				filteredCount++;
				return;
			}
		}
		Group g = groups.FirstOrDefault((Group x) => x.Name == groupName);
		if (g == null)
		{
			g = new Group();
			g.Name = groupName;
			groups.Add(g);
		}
		if (!g.Chs.TryGetValue(chName, out var urls2))
		{
			urls2 = new List<string>();
			g.Chs[chName] = urls2;
		}
		foreach (string url in urls)
		{
			if (!urls2.Contains(url))
			{
				urls2.Add(url);
			}
		}
	}

	private static void IngestTxt(string text, string srcName, string prefix = null)
	{
		string curGroup = "未分组";
		int count = 0;
		string[] lines = text.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("#"))
			{
				continue;
			}
			if (Regex.IsMatch(line, "#genre#", RegexOptions.IgnoreCase))
			{
				curGroup = line.Split(',')[0].Replace("#genre#", "").Replace("#GENRE#", "").Trim();
				if (curGroup.Length == 0)
				{
					curGroup = "未分组";
				}
				continue;
			}
			int idx = line.IndexOf(',');
			if (idx >= 1)
			{
				string chName = line.Substring(0, idx).Trim();
				List<string> urls = (from s in line.Substring(idx + 1).Split('#')
					select s.Trim() into s
					where s.Length > 0
					select s).ToList();
				if (chName.Length > 0 && urls.Count > 0)
				{
					AddCh((prefix == null) ? curGroup : (prefix + curGroup), chName, urls);
					count++;
				}
			}
		}
		Console.WriteLine("[" + srcName + "] TXT 解析: " + count + " 个频道");
	}

	private static void IngestM3u(string text, string srcName)
	{
		string groupName = "未分组";
		string chName = "";
		int count = 0;
		string[] lines = text.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			if (line.Length == 0)
			{
				continue;
			}
			if (line.StartsWith("#EXTINF"))
			{
				Match m = Regex.Match(line, "group-title=\"([^\"]*)\"", RegexOptions.IgnoreCase);
				groupName = ((m.Success && m.Groups[1].Value.Length > 0) ? m.Groups[1].Value : "未分组");
				int idx = line.LastIndexOf(',');
				chName = ((idx >= 0) ? line.Substring(idx + 1).Trim() : "");
			}
			else if (!line.StartsWith("#") && chName.Length > 0 && Regex.IsMatch(line, "^https?://", RegexOptions.IgnoreCase))
			{
				AddCh(groupName, chName, new List<string> { line });
				count++;
				chName = "";
			}
		}
		Console.WriteLine("[" + srcName + "] M3U 解析: " + count + " 个频道");
	}

	private static void IngestJson(string text, string srcName)
	{
		object data;
		try
		{
			data = new JavaScriptSerializer().DeserializeObject(text);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[" + srcName + "] JSON 解析失败: " + ex.Message);
			return;
		}
		int count = JsonWalk(data);
		Console.WriteLine("[" + srcName + "] JSON 解析: " + count + " 个频道");
	}

	private static int JsonWalk(object data)
	{
		int count = 0;
		object[] arr = data as object[];
		Dictionary<string, object> dict = data as Dictionary<string, object>;
		if (arr != null)
		{
			foreach (object gObj in arr)
			{
				count += WalkGroup(gObj);
			}
			return count;
		}
		if (dict != null)
		{
			if (dict.ContainsKey("lives") && dict["lives"] is object[])
			{
				foreach (object obj in (object[])dict["lives"])
				{
					if (!(obj is Dictionary<string, object> live))
					{
						continue;
					}
					if (live.TryGetValue("groups", out var gVal) && gVal is object[])
					{
						count += JsonWalk(gVal);
						continue;
					}
					live.TryGetValue("name", out var liveName);
					if (liveName == null)
					{
						live.TryGetValue("group", out liveName);
					}
					if (live.TryGetValue("channels", out var chVal) && chVal is object[])
					{
						Dictionary<string, object> pseudo = new Dictionary<string, object>();
						pseudo["name"] = liveName;
						pseudo["channel"] = chVal;
						count += WalkGroup(pseudo);
					}
				}
				return count;
			}
			if (dict.ContainsKey("groups"))
			{
				return JsonWalk(dict["groups"]);
			}
		}
		return count;
	}

	private static int WalkGroup(object gObj)
	{
		if (!(gObj is Dictionary<string, object> dict))
		{
			return 0;
		}
		string groupName = "未分组";
		if (dict.TryGetValue("name", out var v) && v is string)
		{
			groupName = (string)v;
		}
		else if (dict.TryGetValue("group", out v) && v is string)
		{
			groupName = (string)v;
		}
		int count = 0;
		object chVal = null;
		if (!dict.TryGetValue("channel", out chVal))
		{
			dict.TryGetValue("channels", out chVal);
		}
		if (!(chVal is object[] arr))
		{
			return 0;
		}
		foreach (object obj in arr)
		{
			if (!(obj is Dictionary<string, object> ch) || !ch.TryGetValue("name", out var chName) || !(chName is string))
			{
				continue;
			}
			List<string> urls = new List<string>();
			if (ch.TryGetValue("urls", out var uVal) && uVal is object[])
			{
				foreach (object u in (object[])uVal)
				{
					if (u is string)
					{
						urls.Add((string)u);
					}
					else if (u is Dictionary<string, object> ud && ud.TryGetValue("url", out var urlStr) && urlStr is string)
					{
						urls.Add((string)urlStr);
					}
				}
			}
			if (urls.Count > 0)
			{
				AddCh(groupName, (string)chName, urls);
				count++;
			}
		}
		return count;
	}

	private static void FetchGulu()
	{
		Console.WriteLine("\n[咕噜] 拉取 咕噜APP 直播源: " + GuluClient.ENDPOINT);
		string deviceId = GuluCrypto.RandomHex(8);
		GuluClient client = new GuluClient(deviceId);
		client.Handshake();
		Console.WriteLine("[咕噜] 握手成功 session=" + Brief(client.Session, 24));
		client.Boot();
		Console.WriteLine("[咕噜] 设备注册 code=0 deviceId=" + deviceId);
		GuluClient.Resp resp = client.Request(3, 76, new byte[0]);
		if (resp.code != 0)
		{
			throw new Exception("直播列表 code=" + resp.code + " " + resp.msg);
		}
		Console.WriteLine("[咕噜] 直播列表 code=0 data=" + ((resp.data != null) ? resp.data.Length : 0) + " 字节");
		List<GuluClient.LiveCh> channels = GuluClient.ParseLive(resp.data);
		foreach (GuluClient.LiveCh ch in channels)
		{
			AddCh("咕噜·" + ch.group, ch.name, new List<string> { ch.url });
		}
		int g = 0;
		int c = 0;
		int u = 0;
		foreach (Group group in groups)
		{
			if (!group.Name.StartsWith("咕噜·"))
			{
				continue;
			}
			g++;
			c += group.Chs.Count;
			foreach (List<string> v in group.Chs.Values)
			{
				u += v.Count;
			}
		}
		Console.WriteLine("[咕噜] 入库: 分组 " + g + " | 频道 " + c + " | 线路 " + u);
	}

	private static string Brief(string s, int n)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		if (s.Length > n)
		{
			return s.Substring(0, n) + "...";
		}
		return s;
	}

	private static void FetchLanlu()
	{
		Console.WriteLine("\n[澜露] 拉取 央卫订阅: " + LANLU_URL);
		byte[] data = null;
		try
		{
			data = HttpGetBytes(LANLU_URL);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[澜露] 订阅下载失败: " + ex.Message);
		}
		if (data == null)
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LANLU_LOCAL);
			if (!File.Exists(path))
			{
				path = LANLU_LOCAL;
			}
			if (!File.Exists(path))
			{
				throw new Exception("网络不可达且找不到本地文件 " + LANLU_LOCAL);
			}
			Console.WriteLine("[澜露] 回退本地文件: " + Path.GetFullPath(path));
			data = File.ReadAllBytes(path);
		}
		IngestTxt(DecodeBytes(data), "澜露", "澜露·");
		int g = 0;
		int c = 0;
		int u = 0;
		foreach (Group group in groups)
		{
			if (!group.Name.StartsWith("澜露·"))
			{
				continue;
			}
			g++;
			c += group.Chs.Count;
			foreach (List<string> v in group.Chs.Values)
			{
				u += v.Count;
			}
		}
		if (g == 0)
		{
			throw new Exception("未解析到任何频道");
		}
		Console.WriteLine("[澜露] 入库: 分组 " + g + " | 频道 " + c + " | 线路 " + u);
	}

	private static string Esc(string s)
	{
		StringBuilder sb = new StringBuilder();
		foreach (char c in s)
		{
			switch (c)
			{
			case '"':
				sb.Append("\\\"");
				continue;
			case '\\':
				sb.Append("\\\\");
				continue;
			case '\b':
				sb.Append("\\b");
				continue;
			case '\f':
				sb.Append("\\f");
				continue;
			case '\n':
				sb.Append("\\n");
				continue;
			case '\r':
				sb.Append("\\r");
				continue;
			case '\t':
				sb.Append("\\t");
				continue;
			}
			if (c < ' ')
			{
				sb.Append("\\u").Append(((int)c).ToString("x4"));
			}
			else
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	private static string BuildJson(List<Group> gs, string epg)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("{\n  \"lives\": [{\n");
		sb.Append("    \"name\": \"直播\",\n");
		if (!string.IsNullOrEmpty(epg))
		{
			sb.Append("    \"epg\": \"").Append(Esc(epg)).Append("\",\n");
		}
		sb.Append("    \"groups\": [");
		bool firstG = true;
		foreach (Group g in gs)
		{
			if (!firstG)
			{
				sb.Append(",");
			}
			firstG = false;
			sb.Append("\n      {\n        \"name\": \"").Append(Esc(g.Name)).Append("\",\n        \"channel\": [");
			bool firstC = true;
			foreach (KeyValuePair<string, List<string>> ch in g.Chs)
			{
				if (!firstC)
				{
					sb.Append(",");
				}
				firstC = false;
				sb.Append("\n          {\n            \"name\": \"").Append(Esc(ch.Key)).Append("\",\n            \"urls\": [");
				bool firstU = true;
				foreach (string url in ch.Value)
				{
					if (!firstU)
					{
						sb.Append(",");
					}
					firstU = false;
					sb.Append("\n              \"").Append(Esc(url)).Append("\"");
				}
				sb.Append("\n            ]\n          }");
			}
			sb.Append("\n        ]\n      }");
		}
		sb.Append("\n    ]\n  }]\n}");
		return sb.ToString();
	}
}

internal static class GuluProto
{
	public class F
	{
		public int f;

		public int w;

		public long vi;

		public byte[] bb;

		public F(int f, int w, long vi, byte[] bb)
		{
			this.f = f;
			this.w = w;
			this.vi = vi;
			this.bb = bb;
		}
	}

	public static byte[] Vint(long n)
	{
		ulong v = (ulong)n;
		using MemoryStream ms = new MemoryStream();
		do
		{
			uint b = (uint)(v & 0x7F);
			v >>= 7;
			if (v != 0)
			{
				b |= 0x80u;
			}
			ms.WriteByte((byte)b);
		}
		while (v != 0);
		return ms.ToArray();
	}

	public static byte[] Tag(int field, int wire)
	{
		return Vint((long)(((ulong)(uint)field << 3) | (uint)wire));
	}

	public static byte[] V(int f, long val)
	{
		return Concat(Tag(f, 0), Vint(val));
	}

	public static byte[] S(int f, string str)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(str);
		return Concat(Tag(f, 2), Concat(Vint(bytes.Length), bytes));
	}

	public static byte[] B(int f, byte[] bytes)
	{
		if (bytes == null)
		{
			bytes = new byte[0];
		}
		return Concat(Tag(f, 2), Concat(Vint(bytes.Length), bytes));
	}

	public static byte[] M(int f, byte[] msg)
	{
		return B(f, msg);
	}

	public static byte[] Slice(byte[] src, int off, int len)
	{
		if (src == null || off >= src.Length || len <= 0)
		{
			return new byte[0];
		}
		if (off < 0)
		{
			off = 0;
		}
		if (off + len > src.Length)
		{
			len = src.Length - off;
		}
		byte[] dst = new byte[len];
		Buffer.BlockCopy(src, off, dst, 0, len);
		return dst;
	}

	public static byte[] Concat(params byte[][] arrs)
	{
		int total = 0;
		foreach (byte[] a in arrs)
		{
			total += a.Length;
		}
		byte[] dst = new byte[total];
		int pos = 0;
		foreach (byte[] a in arrs)
		{
			Buffer.BlockCopy(a, 0, dst, pos, a.Length);
			pos += a.Length;
		}
		return dst;
	}

	public static List<F> Decode(byte[] b)
	{
		List<F> list = new List<F>();
		int pos = 0;
		while (pos < b.Length)
		{
			long[] tag = ReadVint(b, pos);
			pos = (int)tag[1];
			int field = (int)(tag[0] >> 3);
			int wire = (int)(tag[0] & 7);
			if (field == 0)
			{
				throw new Exception("bad field 0");
			}
			switch (wire)
			{
			case 0:
			{
				long[] v = ReadVint(b, pos);
				pos = (int)v[1];
				list.Add(new F(field, wire, v[0], null));
				break;
			}
			case 1:
				if (pos + 8 > b.Length)
				{
					throw new Exception("truncated");
				}
				list.Add(new F(field, wire, 0L, Slice(b, pos, 8)));
				pos += 8;
				break;
			case 2:
			{
				long[] len = ReadVint(b, pos);
				pos = (int)len[1];
				int n = (int)len[0];
				if (n < 0 || pos + n > b.Length)
				{
					throw new Exception("truncated");
				}
				list.Add(new F(field, wire, 0L, Slice(b, pos, n)));
				pos += n;
				break;
			}
			case 5:
				if (pos + 4 > b.Length)
				{
					throw new Exception("truncated");
				}
				list.Add(new F(field, wire, 0L, Slice(b, pos, 4)));
				pos += 4;
				break;
			default:
				throw new Exception("bad wire " + wire);
			}
		}
		return list;
	}

	private static long[] ReadVint(byte[] b, int o)
	{
		long v = 0L;
		int shift = 0;
		int pos = o;
		while (true)
		{
			if (pos >= b.Length)
			{
				throw new Exception("truncated varint");
			}
			int c = b[pos++];
			v |= (long)(c & 0x7F) << shift;
			if ((c & 0x80) == 0)
			{
				break;
			}
			shift += 7;
		}
		return new long[2] { v, pos };
	}

	public static F Find(List<F> fs, int field, int wire)
	{
		if (fs == null)
		{
			return null;
		}
		foreach (F f in fs)
		{
			if (f.f == field && f.w == wire)
			{
				return f;
			}
		}
		return null;
	}

	public static List<F> All(List<F> fs, int field)
	{
		List<F> list = new List<F>();
		if (fs == null)
		{
			return list;
		}
		foreach (F f in fs)
		{
			if (f.f == field)
			{
				list.Add(f);
			}
		}
		return list;
	}

	public static string LooksText(byte[] b)
	{
		if (b == null || b.Length == 0)
		{
			return null;
		}
		string s;
		try
		{
			s = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(b);
		}
		catch
		{
			return null;
		}
		foreach (char c in s)
		{
			if (c < '\0' || c > '\b')
			{
				switch (c)
				{
				case '\v':
				case '\f':
				case '\u000e':
				case '\u000f':
				case '\u0010':
				case '\u0011':
				case '\u0012':
				case '\u0013':
				case '\u0014':
				case '\u0015':
				case '\u0016':
				case '\u0017':
				case '\u0018':
				case '\u0019':
				case '\u001a':
				case '\u001b':
				case '\u001c':
				case '\u001d':
				case '\u001e':
				case '\u001f':
					break;
				default:
					continue;
				}
			}
			return null;
		}
		return s;
	}

	public static string Text(F x)
	{
		if (x == null || x.w != 2)
		{
			return null;
		}
		return LooksText(x.bb);
	}
}

internal static class GuluCrypto
{
	private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

	private static readonly BigInteger P = Big("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");

	private static readonly BigInteger A = P - 3;

	private static readonly BigInteger Gx = Big("6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296");

	private static readonly BigInteger Gy = Big("4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5");

	private static readonly BigInteger N = Big("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");

	public static byte[] RandomBytes(int n)
	{
		byte[] buf = new byte[n];
		Rng.GetBytes(buf);
		return buf;
	}

	public static string RandomHex(int nBytes)
	{
		StringBuilder sb = new StringBuilder();
		byte[] buf = RandomBytes(nBytes);
		foreach (byte b in buf)
		{
			sb.Append(b.ToString("x2"));
		}
		return sb.ToString();
	}

	private static BigInteger Big(string hex)
	{
		byte[] be = new byte[hex.Length / 2];
		for (int i = 0; i < be.Length; i++)
		{
			be[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		}
		return FromBE(be);
	}

	private static BigInteger FromBE(byte[] be)
	{
		byte[] le = new byte[be.Length + 1];
		for (int i = 0; i < be.Length; i++)
		{
			le[i] = be[be.Length - 1 - i];
		}
		le[be.Length] = 0;
		return new BigInteger(le);
	}

	private static byte[] ToBE32(BigInteger v)
	{
		byte[] le = v.ToByteArray();
		int len = le.Length;
		while (len > 1 && le[len - 1] == 0)
		{
			len--;
		}
		byte[] be = new byte[32];
		int n = Math.Min(len, 32);
		for (int i = 0; i < n; i++)
		{
			be[31 - i] = le[i];
		}
		return be;
	}

	public static byte[] DeflateZlib(byte[] data)
	{
		byte[] raw = RawDeflate(data);
		byte[] z = new byte[raw.Length + 6];
		z[0] = 120;
		z[1] = 156;
		Buffer.BlockCopy(raw, 0, z, 2, raw.Length);
		uint adler = Adler32(data);
		z[z.Length - 4] = (byte)(adler >> 24);
		z[z.Length - 3] = (byte)(adler >> 16);
		z[z.Length - 2] = (byte)(adler >> 8);
		z[z.Length - 1] = (byte)adler;
		return z;
	}

	public static byte[] InflateZlib(byte[] data)
	{
		if (data.Length < 6)
		{
			throw new Exception("zlib 数据过短");
		}
		return RawInflate(GuluProto.Slice(data, 2, data.Length - 6));
	}

	public static byte[] RawDeflate(byte[] data)
	{
		using MemoryStream ms = new MemoryStream();
		using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress))
		{
			ds.Write(data, 0, data.Length);
		}
		return ms.ToArray();
	}

	public static byte[] RawInflate(byte[] data)
	{
		using MemoryStream stream = new MemoryStream(data);
		using DeflateStream ds = new DeflateStream(stream, CompressionMode.Decompress);
		using MemoryStream ms = new MemoryStream();
		ds.CopyTo(ms);
		return ms.ToArray();
	}

	private static uint Adler32(byte[] data)
	{
		uint a = 1u;
		uint b = 0u;
		foreach (byte x in data)
		{
			a = (a + x) % 65521;
			b = (b + a) % 65521;
		}
		return (b << 16) | a;
	}

	public static byte[] EcNewKeyPair(out BigInteger priv)
	{
		byte[] seed = new byte[32];
		Rng.GetBytes(seed);
		priv = FromBE(seed) % (N - 1) + 1;
		BigInteger[] pub = EcMul(priv, new BigInteger[2] { Gx, Gy });
		byte[] raw = new byte[65];
		raw[0] = 4;
		Buffer.BlockCopy(ToBE32(pub[0]), 0, raw, 1, 32);
		Buffer.BlockCopy(ToBE32(pub[1]), 0, raw, 33, 32);
		return raw;
	}

	public static byte[] EcShared(BigInteger priv, byte[] peerRaw65)
	{
		if (peerRaw65 == null || peerRaw65.Length != 65 || peerRaw65[0] != 4)
		{
			throw new Exception("无效的 EC 公钥");
		}
		BigInteger px = FromBE(GuluProto.Slice(peerRaw65, 1, 32));
		BigInteger py = FromBE(GuluProto.Slice(peerRaw65, 33, 32));
		BigInteger[] pt = EcMul(priv, new BigInteger[2] { px, py });
		if (pt == null)
		{
			throw new Exception("ECDH 得到无穷远点");
		}
		return ToBE32(pt[0]);
	}

	private static BigInteger EcMod(BigInteger v)
	{
		BigInteger r = v % P;
		if (r.Sign < 0)
		{
			return r + P;
		}
		return r;
	}

	private static BigInteger EcInv(BigInteger v)
	{
		return BigInteger.ModPow(EcMod(v), P - 2, P);
	}

	private static BigInteger[] EcAdd(BigInteger[] p1, BigInteger[] p2)
	{
		if (p1 == null)
		{
			return p2;
		}
		if (p2 == null)
		{
			return p1;
		}
		BigInteger x1 = p1[0];
		BigInteger y1 = p1[1];
		BigInteger x2 = p2[0];
		BigInteger y2 = p2[1];
		if (x1 == x2)
		{
			if (EcMod(y1 + y2) == BigInteger.Zero)
			{
				return null;
			}
			BigInteger lam = EcMod(EcMod(3 * x1 * x1 + A) * EcInv(EcMod(2 * y1)));
			BigInteger x3 = EcMod(lam * lam - 2 * x1);
			BigInteger y3 = EcMod(lam * (x1 - x3) - y1);
			return new BigInteger[2] { x3, y3 };
		}
		BigInteger lam2 = EcMod(EcMod(y2 - y1) * EcInv(EcMod(x2 - x1)));
		BigInteger x3b = EcMod(lam2 * lam2 - x1 - x2);
		BigInteger y3b = EcMod(lam2 * (x1 - x3b) - y1);
		return new BigInteger[2] { x3b, y3b };
	}

	private static BigInteger[] EcMul(BigInteger k, BigInteger[] pt)
	{
		BigInteger[] r = null;
		BigInteger[] q = pt;
		while (k.Sign > 0)
		{
			if (!k.IsEven)
			{
				r = EcAdd(r, q);
			}
			q = EcAdd(q, q);
			k >>= 1;
		}
		return r;
	}

	public static byte[] HmacSha256(byte[] key, byte[] msg)
	{
		using HMACSHA256 hmac = new HMACSHA256(key);
		return hmac.ComputeHash(msg);
	}

	public static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain)
	{
		using Aes aes = Aes.Create();
		aes.Key = key;
		aes.IV = iv;
		aes.Mode = CipherMode.CBC;
		aes.Padding = PaddingMode.PKCS7;
		using ICryptoTransform enc = aes.CreateEncryptor();
		return enc.TransformFinalBlock(plain, 0, plain.Length);
	}

	public static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] ct)
	{
		using Aes aes = Aes.Create();
		aes.Key = key;
		aes.IV = iv;
		aes.Mode = CipherMode.CBC;
		aes.Padding = PaddingMode.PKCS7;
		using ICryptoTransform dec = aes.CreateDecryptor();
		return dec.TransformFinalBlock(ct, 0, ct.Length);
	}

	public static byte[] AesGcmEncrypt(byte[] key, byte[] nonce, byte[] plain)
	{
		using Aes aes = Aes.Create();
		aes.Key = key;
		aes.Mode = CipherMode.ECB;
		aes.Padding = PaddingMode.None;
		using ICryptoTransform enc = aes.CreateEncryptor();
		byte[] h = AesBlock(enc, new byte[16]);
		byte[] j0 = J0(nonce);
		byte[] ct = GcmCtr(enc, j0, plain);
		byte[] s = Ghash(h, ct);
		byte[] e0 = AesBlock(enc, j0);
		byte[] outBuf = new byte[ct.Length + 16];
		Buffer.BlockCopy(ct, 0, outBuf, 0, ct.Length);
		for (int i = 0; i < 16; i++)
		{
			outBuf[ct.Length + i] = (byte)(s[i] ^ e0[i]);
		}
		return outBuf;
	}

	public static byte[] AesGcmDecrypt(byte[] key, byte[] nonce, byte[] ctWithTag)
	{
		if (ctWithTag.Length < 16)
		{
			throw new Exception("GCM 密文过短");
		}
		int ctLen = ctWithTag.Length - 16;
		byte[] ct = GuluProto.Slice(ctWithTag, 0, ctLen);
		byte[] tag = GuluProto.Slice(ctWithTag, ctLen, 16);
		using Aes aes = Aes.Create();
		aes.Key = key;
		aes.Mode = CipherMode.ECB;
		aes.Padding = PaddingMode.None;
		using ICryptoTransform enc = aes.CreateEncryptor();
		byte[] h = AesBlock(enc, new byte[16]);
		byte[] j0 = J0(nonce);
		byte[] s = Ghash(h, ct);
		byte[] e0 = AesBlock(enc, j0);
		for (int i = 0; i < 16; i++)
		{
			if (tag[i] != (byte)(s[i] ^ e0[i]))
			{
				throw new Exception("GCM 校验失败");
			}
		}
		return GcmCtr(enc, j0, ct);
	}

	private static byte[] J0(byte[] nonce)
	{
		byte[] j0 = new byte[16];
		Buffer.BlockCopy(nonce, 0, j0, 0, 12);
		j0[15] = 1;
		return j0;
	}

	private static byte[] AesBlock(ICryptoTransform enc, byte[] block)
	{
		byte[] outBuf = new byte[16];
		enc.TransformBlock(block, 0, 16, outBuf, 0);
		return outBuf;
	}

	private static byte[] Ghash(byte[] h, byte[] ct)
	{
		int pad = (16 - ct.Length % 16) % 16;
		byte[] buf = new byte[ct.Length + pad + 16];
		Buffer.BlockCopy(ct, 0, buf, 0, ct.Length);
		ulong bits = (ulong)ct.Length * 8uL;
		for (int i = 0; i < 8; i++)
		{
			buf[buf.Length - 8 + i] = (byte)(bits >> 56 - 8 * i);
		}
		byte[] y = new byte[16];
		for (int j = 0; j < buf.Length; j += 16)
		{
			for (int k = 0; k < 16; k++)
			{
				y[k] ^= buf[j + k];
			}
			y = GfMul(y, h);
		}
		return y;
	}

	private static byte[] GfMul(byte[] x, byte[] y)
	{
		byte[] z = new byte[16];
		byte[] v = (byte[])y.Clone();
		for (int i = 0; i < 128; i++)
		{
			if ((x[i >> 3] & (128 >> (i & 7))) != 0)
			{
				for (int j = 0; j < 16; j++)
				{
					z[j] ^= v[j];
				}
			}
			bool lsb = (v[15] & 1) != 0;
			for (int n = 15; n > 0; n--)
			{
				v[n] = (byte)((v[n] >> 1) | ((v[n - 1] & 1) << 7));
			}
			v[0] >>= 1;
			if (lsb)
			{
				v[0] ^= 225;
			}
		}
		return z;
	}

	private static void Inc32(byte[] ctr)
	{
		for (int i = 15; i >= 12; i--)
		{
			ctr[i]++;
			if (ctr[i] != 0)
			{
				break;
			}
		}
	}

	private static byte[] GcmCtr(ICryptoTransform enc, byte[] j0, byte[] data)
	{
		byte[] outBuf = new byte[data.Length];
		byte[] ctr = (byte[])j0.Clone();
		for (int i = 0; i < data.Length; i += 16)
		{
			Inc32(ctr);
			byte[] ks = AesBlock(enc, ctr);
			int n = Math.Min(16, data.Length - i);
			for (int k = 0; k < n; k++)
			{
				outBuf[i + k] = (byte)(data[i + k] ^ ks[k]);
			}
		}
		return outBuf;
	}
}

internal class GuluClient
{
	public class Resp
	{
		public long code;

		public string msg;

		public byte[] data;
	}

	public class LiveCh
	{
		public string group;

		public string name;

		public string url;
	}

	public const string ENDPOINT = "http://103.45.132.22:19987/app/bn/v2";

	private const string TAG_VERSION = "1.0.0";

	private const long CODE_CLOCK = 10002L;

	public readonly string DeviceId;

	public string Session;

	public byte[] SessionKey;

	private long seq = 1L;

	private long clockOffset;

	public GuluClient(string deviceId)
	{
		DeviceId = deviceId;
	}

	public void Handshake()
	{
		BigInteger priv;
		byte[] ecPub = GuluCrypto.EcNewKeyPair(out priv);
		string hsKey = GuluCrypto.RandomHex(16);
		byte[] hsKeyBytes = Encoding.UTF8.GetBytes(hsKey);
		byte[] hello = GuluProto.V(1, 0L);
		hello = GuluProto.Concat(hello, GuluProto.V(2, 1L), GuluProto.V(3, 1L));
		byte[] hsMsg = GuluProto.B(1, ecPub);
		hsMsg = GuluProto.Concat(hsMsg, GuluProto.S(2, TAG_VERSION), GuluProto.M(3, hello));
		byte[] plain = GuluCrypto.DeflateZlib(hsMsg);
		byte[] iv = GuluCrypto.RandomBytes(16);
		byte[] ct = GuluCrypto.AesCbcEncrypt(hsKeyBytes, iv, plain);
		byte[] body = Encoding.UTF8.GetBytes(Convert.ToBase64String(GuluProto.Concat(iv, ct)));
		Dictionary<string, string> headers = new Dictionary<string, string>();
		headers["X-Handshake-Key"] = hsKey;
		byte[] respBytes = HttpPost(body, headers);
		byte[] respBuf = Convert.FromBase64String(Encoding.UTF8.GetString(respBytes).Trim());
		byte[] decoded = GuluCrypto.InflateZlib(GuluCrypto.AesCbcDecrypt(hsKeyBytes, GuluProto.Slice(respBuf, 0, 16), GuluProto.Slice(respBuf, 16, respBuf.Length - 16)));
		List<GuluProto.F> fs = GuluProto.Decode(decoded);
		GuluProto.F fSession = GuluProto.Find(fs, 1, 2);
		GuluProto.F fPub = GuluProto.Find(fs, 2, 2);
		if (fSession == null || fPub == null || fPub.bb.Length != 65 || fPub.bb[0] != 4)
		{
			throw new Exception("握手响应结构异常");
		}
		Session = Encoding.UTF8.GetString(fSession.bb);
		byte[] shared = GuluCrypto.EcShared(priv, fPub.bb);
		byte[] k1 = GuluCrypto.HmacSha256(Encoding.UTF8.GetBytes(Session), shared);
		SessionKey = GuluCrypto.HmacSha256(k1, GuluProto.Concat(Encoding.UTF8.GetBytes("v2-session"), new byte[1] { 1 }));
	}

	public void Boot()
	{
		Resp resp = Request(1, 0, BuildBootPayload(DeviceId));
		if (resp.code != 0)
		{
			throw new Exception("设备注册失败 code=" + resp.code + " " + resp.msg);
		}
	}

	public Resp Request(int cat, int method, byte[] payload)
	{
		if (Session == null || SessionKey == null)
		{
			Handshake();
		}
		Resp resp = SendOnce(cat, method, payload);
		if (resp.code == CODE_CLOCK)
		{
			resp = SendOnce(cat, method, payload);
		}
		return resp;
	}

	private Resp SendOnce(int cat, int method, byte[] payload)
	{
		long seqVal = seq++;
		byte[] plain = GuluProto.V(1, seqVal);
		plain = GuluProto.Concat(plain, GuluProto.V(2, cat), GuluProto.V(3, method), GuluProto.B(4, Encoding.UTF8.GetBytes(DeviceId)));
		plain = GuluProto.Concat(plain, GuluProto.B(5, new byte[0]));
		plain = GuluProto.Concat(plain, GuluProto.B(6, payload));
		plain = GuluProto.Concat(plain, GuluProto.V(7, (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds + clockOffset));
		byte[] deflated = GuluCrypto.RawDeflate(plain);
		byte[] nonce = GuluCrypto.RandomBytes(12);
		byte[] ct = GuluCrypto.AesGcmEncrypt(SessionKey, nonce, deflated);
		byte[] body = GuluProto.Concat(GuluProto.V(1, seqVal), GuluProto.B(2, GuluProto.Concat(nonce, ct)));
		Dictionary<string, string> headers = new Dictionary<string, string>();
		headers["X-Session-Id"] = Session;
		headers["X-Player-Page-Protection"] = "1";
		byte[] respBytes = HttpPost(body, headers);
		List<GuluProto.F> fs = GuluProto.Decode(respBytes);
		GuluProto.F fEnc = GuluProto.Find(fs, 2, 2);
		if (fEnc == null)
		{
			throw new Exception("响应无 field2 密文");
		}
		byte[] nonce2 = GuluProto.Slice(fEnc.bb, 0, 12);
		byte[] ct2 = GuluProto.Slice(fEnc.bb, 12, fEnc.bb.Length - 12);
		byte[] decoded = GuluCrypto.RawInflate(GuluCrypto.AesGcmDecrypt(SessionKey, nonce2, ct2));
		List<GuluProto.F> fs2 = GuluProto.Decode(decoded);
		Resp resp = new Resp();
		resp.code = GuluProto.Find(fs2, 2, 0)?.vi ?? 0;
		GuluProto.F fMsg = GuluProto.Find(fs2, 3, 2);
		resp.msg = ((fMsg == null) ? "" : Encoding.UTF8.GetString(fMsg.bb));
		resp.data = GuluProto.Find(fs2, 4, 2)?.bb;
		return resp;
	}

	public static byte[] BuildBootPayload(string deviceId)
	{
		byte[] appInfo = GuluProto.S(1, "咕噜咕噜");
		appInfo = GuluProto.Concat(appInfo, GuluProto.S(2, "2.1.2"), GuluProto.S(3, "com.jymqfh.xee"), GuluProto.S(4, deviceId), GuluProto.S(5, "212"));
		byte[] devInfo = GuluProto.S(1, deviceId);
		devInfo = GuluProto.Concat(devInfo, GuluProto.V(2, 0L), GuluProto.S(3, "11"), GuluProto.S(4, "Pixel 4"), GuluProto.S(5, "google/redfin/redfin:11/RQ3A.211001.001"), GuluProto.S(6, "google"), GuluProto.S(7, "redfin"), GuluProto.V(8, 0L), GuluProto.S(9, "unknown"), GuluProto.S(10, "unknown"), GuluProto.V(11, 0L), GuluProto.V(12, 30L));
		byte[] plain = GuluProto.S(1, "v1");
		return GuluProto.Concat(plain, GuluProto.S(2, "Android 11"), GuluProto.S(3, "gulu"), GuluProto.M(4, appInfo), GuluProto.M(5, devInfo));
	}

	public static List<LiveCh> ParseLive(byte[] data)
	{
		List<LiveCh> list = new List<LiveCh>();
		if (data == null || data.Length == 0)
		{
			return list;
		}
		List<GuluProto.F> fs;
		try
		{
			fs = GuluProto.Decode(data);
		}
		catch
		{
			return list;
		}
		foreach (GuluProto.F item in GuluProto.All(fs, 2))
		{
			if (item.w != 2)
			{
				continue;
			}
			List<GuluProto.F> fsPage;
			try
			{
				List<GuluProto.F> tmp = GuluProto.Decode(item.bb);
				GuluProto.F inner = GuluProto.Find(tmp, 1, 2);
				fsPage = ((inner == null) ? tmp : GuluProto.Decode(inner.bb));
			}
			catch
			{
				continue;
			}
			string pageName = GuluProto.Text(GuluProto.Find(fsPage, 5, 2));
			if (pageName == null)
			{
				pageName = GuluProto.Text(GuluProto.Find(fsPage, 6, 2));
			}
			if (pageName == null)
			{
				pageName = GuluProto.Text(GuluProto.Find(fsPage, 12, 2));
			}
			if (pageName == null || pageName.Length == 0)
			{
				pageName = "直播";
			}
			foreach (GuluProto.F item2 in GuluProto.All(fsPage, 75))
			{
				if (item2.w != 2)
				{
					continue;
				}
				List<GuluProto.F> fsCat;
				try
				{
					fsCat = GuluProto.Decode(item2.bb);
				}
				catch
				{
					continue;
				}
				foreach (GuluProto.F item3 in GuluProto.All(fsCat, 2))
				{
					if (item3.w != 2)
					{
						continue;
					}
					List<GuluProto.F> fsCh;
					try
					{
						fsCh = GuluProto.Decode(item3.bb);
					}
					catch
					{
						continue;
					}
					string url = GuluProto.Text(GuluProto.Find(fsCh, 3, 2));
					string name = GuluProto.Text(GuluProto.Find(fsCh, 4, 2));
					if (url != null && url.Length != 0)
					{
						if (name == null || name.Length == 0)
						{
							name = "未命名频道";
						}
						list.Add(new LiveCh
						{
							group = pageName,
							name = name,
							url = url
						});
					}
				}
			}
		}
		return list;
	}

	private byte[] HttpPost(byte[] body, Dictionary<string, string> headers)
	{
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ENDPOINT);
		req.Method = "POST";
		req.UserAgent = "okhttp/3.12.0";
		req.ContentType = "application/x-protobuf";
		req.Timeout = 20000;
		req.ReadWriteTimeout = 20000;
		req.Proxy = null;
		req.ServicePoint.Expect100Continue = false;
		if (headers != null)
		{
			foreach (KeyValuePair<string, string> kv in headers)
			{
				req.Headers[kv.Key] = kv.Value;
			}
		}
		req.ContentLength = body.Length;
		using (Stream stream = req.GetRequestStream())
		{
			stream.Write(body, 0, body.Length);
		}
		try
		{
			using HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
			SyncClock(resp);
			using MemoryStream ms = new MemoryStream();
			using Stream respStream = resp.GetResponseStream();
			byte[] buf = new byte[65536];
			int count;
			while ((count = respStream.Read(buf, 0, buf.Length)) > 0)
			{
				ms.Write(buf, 0, count);
			}
			return ms.ToArray();
		}
		catch (WebException ex)
		{
			if (ex.Response is HttpWebResponse errResp)
			{
				throw new Exception("HTTP " + (int)errResp.StatusCode + " " + errResp.StatusDescription);
			}
			throw;
		}
	}

	private void SyncClock(HttpWebResponse resp)
	{
		try
		{
			string date = resp.Headers[HttpResponseHeader.Date];
			if (!string.IsNullOrEmpty(date) && DateTime.TryParseExact(date, "R", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var serverTime))
			{
				clockOffset = (long)(serverTime - DateTime.UtcNow).TotalMilliseconds;
			}
		}
		catch
		{
		}
	}
}
