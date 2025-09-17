using Android.App;
using Android.Content.PM;
using AndroidIntent = Android.Content.Intent;

namespace Yeetmedia3.Platforms.Android;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { AndroidIntent.ActionView },
    Categories = new[] { AndroidIntent.CategoryDefault, AndroidIntent.CategoryBrowsable },
    DataScheme = "com.companyname.yeetmedia3")]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}