import { defineConfig, devices } from '@playwright/test'

const webBaseUrl = process.env.E2E_BASE_URL ?? 'http://localhost:5173'
const apiBaseUrl = process.env.E2E_API_URL ?? 'http://localhost:5180'

export default defineConfig({
  testDir: './specs',
  timeout: 45_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: webBaseUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'pt-PT',
  },
  projects: [
    { name: 'chromium-desktop', use: { ...devices['Desktop Chrome'] } },
    {
      name: 'chromium-mobile',
      use: { ...devices['Pixel 7'] },

      // As capturas são o retrato de secretária, e os dois perfis escrevem nos mesmos ficheiros:
      // corridas em ambos, o último a passar ficaria com a documentação, e o painel visto num
      // telemóvel passaria a fazer de retrato de secretária. O que precisa de ser verificado ao
      // tamanho do telemóvel são os testes funcionais, que correm nos dois perfis.
      testIgnore: /(-capture|screenshots)\.spec\.ts$/,
    },
  ],
  webServer: [
    {
      // The API needs the SQL Server container from docker-compose to be running.
      command: 'dotnet run --project ../../src/Api/DocOps.Api.csproj --launch-profile http',
      url: `${apiBaseUrl}/health/ready`,
      reuseExistingServer: true,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      // A suíte corre contra o que é entregue, e não contra o servidor de desenvolvimento. Um
      // servidor que transforma módulos a pedido, mantém uma ligação de recarregamento aberta e
      // reinicia sozinho quando a configuração muda não é o que ninguém publica, e uma suíte verde
      // contra ele não diz nada sobre o que foi compilado. A pré-visualização serve ficheiros
      // estáticos: nada disso existe. O build leva menos de um segundo, portanto não custa.
      //
      // `E2E_DEV=1` volta ao servidor de desenvolvimento, para quando se quer acompanhar um teste
      // com o código a mudar por baixo.
      command: process.env.E2E_DEV ? 'npm run dev' : 'npm run build && npm run preview',
      cwd: '../../src/Web',
      url: webBaseUrl,

      // Em modo compilado, não reaproveitar nada: reutilizar um servidor de desenvolvimento que já
      // esteja de pé desfaria exatamente o que esta opção existe para garantir, e um servidor de
      // longa duração, com horas de recarregamentos acumulados, torna a suíte instável. Quem pediu
      // `E2E_DEV` pode reaproveitar o seu.
      reuseExistingServer: !!process.env.E2E_DEV,
      timeout: 180_000,
    },
  ],
})
