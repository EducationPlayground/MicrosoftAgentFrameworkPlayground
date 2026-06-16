# Foundry Hosted Agent — linux/amd64 zorunlu (platform x86_64 istiyor)
# docker build --platform linux/amd64 -t customer-agent:v1 .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DeployFoundryCustomerAgent.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Foundry protokolü 8088 portunu bekler
ENV ASPNETCORE_URLS=http://+:8088
EXPOSE 8088

ENTRYPOINT ["dotnet", "DeployFoundryCustomerAgent.dll"]
