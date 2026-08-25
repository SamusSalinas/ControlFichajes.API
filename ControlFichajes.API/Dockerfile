FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["ControlFichajes.API.csproj", "."]
RUN dotnet restore "ControlFichajes.API.csproj"

COPY . .
RUN dotnet publish "ControlFichajes.API.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ControlFichajes.API.dll"]