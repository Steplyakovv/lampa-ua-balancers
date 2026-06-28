namespace UafixApiNew.Settings;
public static class ProxyParams
{
	public static string ProxySourceIps => "https://api.proxyscrape.com/v2/?request=displayproxies&protocol=http&timeout=10000&country=all&utm_source=chatgpt.com";
	public static string WorkerProxy => "https://proxy-worker.s-teplyakovv.workers.dev/?url=";
	public static string[] BalancersNeedProxy => new[] { "https://ashdi.vip", "https://zetvideo.net" };
	public static int BatchSize => 30;
	public static int Timeout => 30;
}
