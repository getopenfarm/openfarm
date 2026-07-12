FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
EXPOSE 8888
ENTRYPOINT ["dotnet", "watch", "run", "--project", "src/OpenFarm.Api/OpenFarm.Api.csproj", "--urls", "http://0.0.0.0:8888", "--no-launch-profile"]
