using HT_Secim_API.Guvenlik;
using HT_Secim_API.Veri;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Veri deposu tekil: JSON onbellegi surum numarasina bagli tutuluyor,
// her istekte yeniden olusturulursa onbellegin anlami kalmaz.
builder.Services.AddSingleton<VeriDeposu>();

// Yonetim panelinin yazma islemleri. Onbellek kullanmiyor, her zaman
// veritabanindaki guncel hali okuyor.
builder.Services.AddScoped<YonetimDeposu>();

builder.Services.AddControllers(secenek =>
{
    // Butun uclar X-API-KEY istiyor.
    secenek.Filters.Add<ApiAnahtariFiltresi>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HT SECIM API", Version = "v1" });

    // Swagger arayuzunden deneyebilmek icin anahtar kutusu.
    c.AddSecurityDefinition(ApiAnahtariFiltresi.BASLIK, new OpenApiSecurityScheme
    {
        Name = ApiAnahtariFiltresi.BASLIK,
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "appsettings.json icindeki HtSecim:ApiAnahtari degeri"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = ApiAnahtariFiltresi.BASLIK
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Yonetim paneli: wwwroot/yonetim/index.html
// Sayfanin kendisi anahtarsiz aciliyor ama tek basina ise yaramiyor;
// icindeki butun istekler X-API-KEY ile gidiyor ve anahtarsiz 401 aliyor.
app.UseDefaultFiles();
app.UseStaticFiles();

// Kok adres dogrudan panele gitsin.
app.MapGet("/", () => Results.Redirect("/yonetim/"));

// HTTPS yonlendirmesi YOK.
// Reji uygulamasi .NET Framework 4.7.2 ve yerel agda duz HTTP ile baglaniyor;
// yonlendirme acik kalirsa gelistirme sertifikasi yuzunden baglanamiyor.
// Gercek dagitimda API sertifikali bir sunucunun arkasina konur, bu dosyada
// bir sey degismez - reji uygulamasinin api dosyasina https adresi yazilir.

app.MapControllers();

app.Run();
