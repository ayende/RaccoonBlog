FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip

COPY RaccoonBlog.sln ./
COPY RaccoonBlog.Web/RaccoonBlog.Web.csproj RaccoonBlog.Web/

RUN dotnet restore RaccoonBlog.Web/RaccoonBlog.Web.csproj

COPY . .

RUN dotnet publish RaccoonBlog.Web/RaccoonBlog.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    ASPNETCORE_ENVIRONMENT=Production

RUN groupadd --system --gid 10001 appgroup && useradd --system --uid 10001 --gid appgroup appuser

COPY --from=build /app/publish ./
COPY --from=build /src/RaccoonBlog.Web/NLog.config ./NLog.config

RUN chown -R appuser:appgroup /app

USER appuser

ENV ASPNETCORE_URLS=http://0.0.0.0:80

EXPOSE 80

ENTRYPOINT ["dotnet", "RaccoonBlog.Web.dll"]