FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Holodeck.Web/Holodeck.Web.csproj src/Holodeck.Web/
RUN dotnet restore src/Holodeck.Web/Holodeck.Web.csproj
COPY src/ src/
RUN dotnet publish src/Holodeck.Web/Holodeck.Web.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# 8080 serves the user; 8081 serves agents, which run in their own containers (see runtime/agent).
ENV ASPNETCORE_HTTP_PORTS=8080;8081
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "Holodeck.Web.dll"]
