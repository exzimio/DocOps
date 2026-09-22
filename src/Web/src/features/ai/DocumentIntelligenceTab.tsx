import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileText, MessageSquareText, Sparkles } from 'lucide-react'
import type { DocumentDetail } from '@/features/documents/types'
import { Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'
import { useDocumentText } from './api'
import { AiReviewPanel } from './AiReviewPanel'
import { AskDocumentPanel } from './AskDocumentPanel'
import { DocumentTextView } from './DocumentTextView'

interface DocumentIntelligenceTabProps {
  document: DocumentDetail
  canProcess: boolean
}

/**
 * The document's text beside what the AI made of it. Clicking any piece of evidence — a suggested
 * value, the classification, a cited answer — brings its line into view on the left.
 */
export function DocumentIntelligenceTab({ document, canProcess }: DocumentIntelligenceTabProps) {
  const { t } = useTranslation()
  const [highlightedLine, setHighlightedLine] = useState<number | null>(null)

  const canReadContent = document.effectiveAccess.levels.includes('download')
  const textQuery = useDocumentText(document.summary.id, canReadContent)

  return (
    <div className="grid items-start gap-4 2xl:grid-cols-2">
      {/* Sticky, so the line a piece of evidence points at is on screen when it is highlighted. */}
      <Card className="2xl:sticky 2xl:top-[4.5rem]">
        <CardHeader
          title={t('ai.text.title')}
          description={
            textQuery.data ? `${textQuery.data.fileName} · v${textQuery.data.versionNumber}` : undefined
          }
          icon={<FileText className="size-4" />}
        />
        <CardBody>
          {!canReadContent && <p className="text-xs text-ink-muted">{t('ai.text.noAccess')}</p>}
          {textQuery.isLoading && <LoadingState label={t('common.loading')} />}
          {textQuery.isError && (
            <ErrorState title={t('errors.genericTitle')} body={textQuery.error.message} />
          )}
          {textQuery.data && <DocumentTextView text={textQuery.data} highlightedLine={highlightedLine} />}
        </CardBody>
      </Card>

      <div className="flex flex-col gap-4">
        <Card>
          <CardHeader title={t('ai.review.title')} icon={<Sparkles className="size-4" />} />
          <CardBody>
            <AiReviewPanel document={document} canProcess={canProcess} onShowLine={setHighlightedLine} />
          </CardBody>
        </Card>

        <Card>
          <CardHeader title={t('ai.ask.title')} icon={<MessageSquareText className="size-4" />} />
          <CardBody>
            <AskDocumentPanel documentId={document.summary.id} onShowLine={setHighlightedLine} />
          </CardBody>
        </Card>
      </div>
    </div>
  )
}
