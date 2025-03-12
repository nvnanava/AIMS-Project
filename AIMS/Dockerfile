FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5119

ENV ASPNETCORE_URLS=http://+:5119

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["AIMS/AIMS.csproj", "AIMS/"]
RUN dotnet restore "AIMS/AIMS.csproj"
COPY . .
WORKDIR "/src/AIMS"
RUN dotnet build "AIMS.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "AIMS.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AIMS.dll"]
