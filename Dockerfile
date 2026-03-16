# 1. Stage: build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем только проектные файлы и восстанавливаем пакеты
COPY *.csproj ./
RUN dotnet restore

# Копируем все файлы проекта
COPY . ./
RUN dotnet publish -c Release -o /app

# 2. Stage: runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Копируем собранное приложение
COPY --from=build /app .

# Указываем порт (Render передаёт свой через переменную PORT)
ENV DOTNET_URLS=http://0.0.0.0:${PORT:-5000}
EXPOSE 5000

# Запуск приложения
ENTRYPOINT ["dotnet", "UafixApiNew.dll"]