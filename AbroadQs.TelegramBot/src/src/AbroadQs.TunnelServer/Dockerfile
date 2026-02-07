FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 9080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/AbroadQs.TunnelServer/AbroadQs.TunnelServer.csproj", "src/AbroadQs.TunnelServer/"]
RUN dotnet restore "src/AbroadQs.TunnelServer/AbroadQs.TunnelServer.csproj"
COPY src/AbroadQs.TunnelServer/ src/AbroadQs.TunnelServer/
RUN dotnet build "src/AbroadQs.TunnelServer/AbroadQs.TunnelServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/AbroadQs.TunnelServer/AbroadQs.TunnelServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:9080
ENTRYPOINT ["dotnet", "AbroadQs.TunnelServer.dll"]
