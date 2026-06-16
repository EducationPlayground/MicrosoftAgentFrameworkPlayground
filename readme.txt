Azure Container Registry (ACR) İşlemleri:

1. Docker İmajını Oluşturma:
docker-compose build deployfoundrycustomeragent

2. Azure Container Registry'ye Giriş:
az acr login --name educationfoundry

3. Docker İmajını ACR'ye Gönderme:
docker push educationfoundry.azurecr.io/deployfoundrycustomeragent:latest
