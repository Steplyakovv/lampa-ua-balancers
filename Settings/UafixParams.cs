namespace UafixApiNew.Settings;
public static class UafixParams
{
	public static string BaseUrl => "https://uafix.net";
	public static string ValidationMessage => "Якщо Ви бачите тільки трейлер або плеєр не працює, тоді пройдіть авторизацію або змініть країну перегляду за допомогою ВПН!";
	public static int LengthShortwords => 4;
	public static int LimitParallelRequest => 10;
	public static int LimitSearchedPage => 5;
}
