# ADR-003 — Armazenamento de documentos fora da base de dados relacional

- **Estado:** aceite (implementação na Fase 2)
- **Data:** 2026-09-18
- **Âmbito:** armazenamento de ficheiros, versões, preparação para a nuvem

## Contexto

A plataforma guarda faturas, contratos e anexos: ficheiros binários de dimensão variável, com várias
versões por documento, que têm de ser descarregáveis, pré-visualizáveis e sujeitos a políticas de
retenção. Os metadados desses ficheiros são fortemente relacionais e têm de ser pesquisáveis.

## Decisão

Separar **metadados** de **conteúdo**:

- Os metadados (documento, versão, tipo, estado, classificação, retenção, permissões) vivem em SQL
  Server.
- O conteúdo binário vive num armazenamento de objetos, atrás da interface `IFileStorage`.
- A ligação entre os dois é a chave de armazenamento guardada na linha da versão, acompanhada do
  tamanho, do tipo de conteúdo e do *hash* SHA-256 do ficheiro.

```csharp
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, FileMetadata metadata, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
```

Implementação local: `LocalFileStorage`, em disco, com as chaves organizadas por organização e
identificador de documento. Implementação preparada: `AzureBlobStorage`.

## Alternativas consideradas

**Coluna `varbinary(max)` na base de dados.** Rejeitado. Aumenta drasticamente o tamanho da base e
dos *backups*, degrada o desempenho das consultas de metadados e faz passar cada descarregamento pela
base de dados.

**`FILESTREAM` / `FileTable` do SQL Server.** Rejeitado. Resolve o problema de desempenho, mas prende
a solução a SQL Server em máquina e complica a migração para armazenamento de objetos em nuvem.

**Armazenamento de objetos sem abstração (SDK do Azure diretamente).** Rejeitado. Impediria o
desenvolvimento e os testes sem conta de nuvem.

## Consequências

**Positivas**
- A base de dados mantém-se pequena, rápida e barata de replicar.
- O mesmo código corre em disco local e em Azure Blob Storage.
- O *hash* por versão permite deteção de duplicados, verificação de integridade e suporte à trilha de
  assinatura eletrónica.

**Negativas**
- Dois sistemas de persistência implicam que uma falha entre a escrita do ficheiro e a da linha pode
  deixar um ficheiro órfão.

**Mitigação:** o ficheiro é escrito primeiro e a linha depois; um ficheiro sem linha é lixo
recuperável por uma tarefa de limpeza do *worker*, ao passo que uma linha sem ficheiro seria um erro
visível ao utilizador. A eliminação é *soft delete* na base de dados, e a remoção física só ocorre
quando a política de retenção o determina.

## Regras de segurança do carregamento (Fase 2)

Tipo MIME permitido por tipo de documento, tamanho máximo configurável, verificação da assinatura do
ficheiro (e não apenas da extensão), nome de ficheiro higienizado e chave de armazenamento gerada
pelo servidor — nunca derivada de entrada do utilizador.
