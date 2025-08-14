namespace google_api_monitor
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("UsageCors", policy =>
                {
                    policy.WithOrigins("https://localhost:7243",
                                        "http://localhost:7243")
                          .AllowAnyHeader()
                          .AllowAnyMethod();

                });
            });
            builder.Services.AddControllersWithViews();

            // 設定 Google Maps API 的相關設定
            builder.Services.AddSingleton<UsageService>();
            builder.Services.Configure<Map.Models.GoogleMapsSettings>(
            builder.Configuration.GetSection("GoogleMaps")); builder.Services.AddSingleton<UsageService>();
            // Add services to the container.

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();
            app.UseCors("UsageCors");
            app.UseAuthorization();
            app.MapControllers();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
