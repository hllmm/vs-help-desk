# VS Help Desk API — multi-stage production image
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
WORKDIR /src
COPY VSHelpDesk.slnx ./
COPY Directory.Packages.props ./
COPY src/VSHelpDesk.Domain/VSHelpDesk.Domain.csproj src/VSHelpDesk.Domain/
COPY src/VSHelpDesk.Application/VSHelpDesk.Application.csproj src/VSHelpDesk.Application/
COPY src/VSHelpDesk.Infrastructure/VSHelpDesk.Infrastructure.csproj src/VSHelpDesk.Infrastructure/
COPY src/VSHelpDesk.WebAPI/VSHelpDesk.WebAPI.csproj src/VSHelpDesk.WebAPI/
COPY src/ src/
# Restore after full source copy so assets match publish (avoids NETSDK1064 in layered builds).
RUN dotnet restore src/VSHelpDesk.WebAPI/VSHelpDesk.WebAPI.csproj
RUN dotnet publish src/VSHelpDesk.WebAPI/VSHelpDesk.WebAPI.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0
RUN useradd --create-home --uid 10001 appuser \
    && mkdir -p /var/vshelpdesk/attachments \
    && chown -R appuser:appuser /var/vshelpdesk /app
COPY --from=build /app/publish .
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "VSHelpDesk.WebAPI.dll"]
