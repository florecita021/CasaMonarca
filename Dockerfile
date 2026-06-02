# Use the official Microsoft .NET SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Copy everything and restore any project dependencies
COPY . ./
RUN dotnet restore CasaMonarcaApp/CasaMonarcaApp.csproj

# Build and publish a clean release container
RUN dotnet publish CasaMonarcaApp/CasaMonarcaApp.csproj -c Release -o out

# Build the runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .

# Tell the server to open port 8080 for web traffic
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CasaMonarcaApp.dll"]
