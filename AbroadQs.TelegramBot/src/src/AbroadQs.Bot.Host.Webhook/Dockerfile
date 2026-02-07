FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5252
ENV ASPNETCORE_URLS=http://+:5252

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/AbroadQs.Bot.Contracts/AbroadQs.Bot.Contracts.csproj", "src/AbroadQs.Bot.Contracts/"]
COPY ["src/AbroadQs.Bot.Application/AbroadQs.Bot.Application.csproj", "src/AbroadQs.Bot.Application/"]
COPY ["src/AbroadQs.Bot.Telegram/AbroadQs.Bot.Telegram.csproj", "src/AbroadQs.Bot.Telegram/"]
COPY ["src/AbroadQs.Bot.Data/AbroadQs.Bot.Data.csproj", "src/AbroadQs.Bot.Data/"]
COPY ["src/AbroadQs.Bot.Modules.Common/AbroadQs.Bot.Modules.Common.csproj", "src/AbroadQs.Bot.Modules.Common/"]
COPY ["src/AbroadQs.Bot.Modules.Example/AbroadQs.Bot.Modules.Example.csproj", "src/AbroadQs.Bot.Modules.Example/"]
COPY ["src/AbroadQs.Bot.Host.Webhook/AbroadQs.Bot.Host.Webhook.csproj", "src/AbroadQs.Bot.Host.Webhook/"]

RUN dotnet restore "src/AbroadQs.Bot.Host.Webhook/AbroadQs.Bot.Host.Webhook.csproj"

COPY src/ src/
RUN dotnet build "src/AbroadQs.Bot.Host.Webhook/AbroadQs.Bot.Host.Webhook.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/AbroadQs.Bot.Host.Webhook/AbroadQs.Bot.Host.Webhook.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AbroadQs.Bot.Host.Webhook.dll"]
