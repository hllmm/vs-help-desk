import {
  useEffect,
  useState,
  type ChangeEvent,
  type FormEvent,
  type ReactElement,
} from 'react'
import {
  useParameters,
  type ParameterLoadErrorKind,
  type ParameterSaveErrorKind,
} from '../features/parameters/useParameters'
import {
  useParameterAudit,
  type ParameterAuditLoadErrorKind,
} from '../features/parameters/useParameterAudit'
import { formatTicketActivity } from '../features/tickets/ticketListModel'

function loadErrorMessage(
  kind: ParameterLoadErrorKind,
  hasRows: boolean,
): string {
  if (hasRows) {
    return kind === 'network'
      ? 'Destek hizmetine ulaşılamadı. Mevcut parametreleri görüntülemeye devam edebilir ve yeniden deneyebilirsiniz.'
      : 'Parametreler güncellenemedi. Mevcut listeyi görüntülemeye devam edebilirsiniz.'
  }

  return kind === 'network'
    ? 'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
    : 'Parametreler yüklenemedi. Lütfen yeniden deneyin.'
}

function saveErrorMessage(kind: ParameterSaveErrorKind): string {
  switch (kind) {
    case 'validation':
      return 'Değer geçersiz. Lütfen kontrol edip yeniden deneyin.'
    case 'not-found':
      return 'Parametre bulunamadı.'
    case 'network':
      return 'Destek hizmetine ulaşılamadı. Bağlantınızı kontrol edip yeniden deneyin.'
    case 'server':
      return 'Parametre kaydedilemedi. Lütfen yeniden deneyin.'
  }
}

function auditErrorMessage(kind: ParameterAuditLoadErrorKind): string {
  return kind === 'network'
    ? 'Destek hizmetine ulaşılamadı. Değişiklik geçmişi yüklenemedi.'
    : 'Değişiklik geçmişi yüklenemedi. Lütfen yeniden deneyin.'
}

export function ParametersPage(): ReactElement {
  const {
    parameters,
    hasLoaded,
    isInitialLoading,
    isRefreshing,
    error,
    refresh,
    savingKey,
    saveParameter,
  } = useParameters()

  const {
    entries: auditEntries,
    hasLoaded: auditHasLoaded,
    isLoading: auditIsLoading,
    error: auditError,
    refresh: refreshAudit,
  } = useParameterAudit(20)

  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [rowError, setRowError] = useState<{
    key: string
    message: string
  } | null>(null)
  const [successKey, setSuccessKey] = useState<string | null>(null)

  useEffect(() => {
    setDrafts((current) => {
      const next = { ...current }
      for (const parameter of parameters) {
        if (!(parameter.key in next)) {
          next[parameter.key] = parameter.value
        }
      }
      return next
    })
  }, [parameters])

  const isBusy = isInitialLoading || isRefreshing
  const hasRows = parameters.length > 0
  const showInitialError = hasLoaded && error !== null && !hasRows
  const showRefreshError = error !== null && hasRows
  const showResults = hasLoaded && (error === null || hasRows) && hasRows
  const showTrueEmpty = hasLoaded && error === null && !hasRows

  function handleDraftChange(key: string, event: ChangeEvent<HTMLInputElement>) {
    const value = event.target.value
    setDrafts((current) => ({ ...current, [key]: value }))
    if (rowError?.key === key) {
      setRowError(null)
    }
    if (successKey === key) {
      setSuccessKey(null)
    }
  }

  async function handleSave(key: string, event: FormEvent) {
    event.preventDefault()
    const value = drafts[key] ?? ''
    setRowError(null)
    setSuccessKey(null)

    const result = await saveParameter(key, value)
    if (result.ok) {
      setDrafts((current) => ({
        ...current,
        [key]: result.parameter.value,
      }))
      setSuccessKey(key)
      void refreshAudit()
      return
    }
    if (result.error === null) {
      return
    }
    setRowError({ key, message: saveErrorMessage(result.error) })
  }

  return (
    <section
      className="parameters-workspace"
      aria-labelledby="parameters-title"
      aria-busy={isBusy || savingKey !== null}
      role="region"
    >
      <header className="parameters-workspace__header">
        <div>
          <h1 id="parameters-title">Parametreler</h1>
          <p className="parameters-workspace__lede">
            Uygulama parametrelerini görüntüleyin ve güncelleyin. Değişiklikler
            bir sonraki işlemde geçerli olur.
          </p>
        </div>
        {hasLoaded && hasRows ? (
          <button
            type="button"
            className="button button--quiet"
            onClick={() => {
              void refresh()
              void refreshAudit()
            }}
            disabled={isBusy || savingKey !== null}
          >
            {isRefreshing ? 'Yenileniyor…' : 'Yenile'}
          </button>
        ) : null}
      </header>

      {isInitialLoading ? (
        <p className="ticket-state ticket-state--loading" role="status">
          Parametreler yükleniyor…
        </p>
      ) : null}

      {showInitialError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{loadErrorMessage(error, false)}</p>
          <button
            type="button"
            className="button button--primary"
            onClick={() => void refresh()}
          >
            Yeniden dene
          </button>
        </div>
      ) : null}

      {showRefreshError && error ? (
        <div className="ticket-state ticket-state--error" role="alert">
          <p>{loadErrorMessage(error, true)}</p>
        </div>
      ) : null}

      {showTrueEmpty ? (
        <div className="ticket-state ticket-state--empty">
          <p>Görüntülenecek parametre yok.</p>
        </div>
      ) : null}

      {showResults ? (
        <div className="parameters-table-view">
          <table className="ticket-table parameters-table">
            <caption className="visually-hidden">Uygulama parametreleri</caption>
            <thead>
              <tr>
                <th scope="col">Anahtar</th>
                <th scope="col">Açıklama</th>
                <th scope="col">Değer</th>
                <th scope="col">Güncellendi</th>
                <th scope="col">Kaydet</th>
              </tr>
            </thead>
            <tbody>
              {parameters.map((parameter) => {
                const draft = drafts[parameter.key] ?? parameter.value
                const isSaving = savingKey === parameter.key
                const showRowSuccess = successKey === parameter.key
                const showRowError = rowError?.key === parameter.key
                const formId = `parameter-form-${parameter.key}`

                return (
                  <tr key={parameter.key}>
                    <th scope="row" className="parameters-table__key">
                      <code>{parameter.key}</code>
                    </th>
                    <td>{parameter.description}</td>
                    <td>
                      <form
                        id={formId}
                        className="parameters-table__value-form"
                        onSubmit={(event) => void handleSave(parameter.key, event)}
                      >
                        <label
                          className="visually-hidden"
                          htmlFor={`parameter-value-${parameter.key}`}
                        >
                          {parameter.key} değeri
                        </label>
                        <input
                          id={`parameter-value-${parameter.key}`}
                          name="value"
                          type="text"
                          className="parameters-table__input"
                          value={draft}
                          onChange={(event) =>
                            handleDraftChange(parameter.key, event)
                          }
                          disabled={isSaving}
                          autoComplete="off"
                        />
                      </form>
                      {showRowSuccess ? (
                        <p
                          className="notice notice--info parameters-table__notice"
                          role="status"
                        >
                          Parametre kaydedildi.
                        </p>
                      ) : null}
                      {showRowError && rowError ? (
                        <p
                          className="notice notice--error parameters-table__notice"
                          role="alert"
                        >
                          {rowError.message}
                        </p>
                      ) : null}
                    </td>
                    <td>
                      <time dateTime={parameter.updatedAt}>
                        {formatTicketActivity(parameter.updatedAt)}
                      </time>
                    </td>
                    <td className="parameters-table__action-cell">
                      <button
                        type="submit"
                        form={formId}
                        className="button button--primary parameters-table__save"
                        disabled={isSaving || isBusy}
                      >
                        {isSaving ? 'Kaydediliyor…' : 'Kaydet'}
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      ) : null}

      <details className="parameters-audit" open>
        <summary className="parameters-audit__summary">Son değişiklikler</summary>
        {auditIsLoading && !auditHasLoaded ? (
          <p className="ticket-state ticket-state--loading" role="status">
            Değişiklik geçmişi yükleniyor…
          </p>
        ) : null}
        {auditError ? (
          <div className="ticket-state ticket-state--error" role="alert">
            <p>{auditErrorMessage(auditError)}</p>
            <button
              type="button"
              className="button button--primary"
              onClick={() => void refreshAudit()}
            >
              Yeniden dene
            </button>
          </div>
        ) : null}
        {auditHasLoaded && !auditError && auditEntries.length === 0 ? (
          <p className="parameters-audit__empty">Henüz kayıtlı değişiklik yok.</p>
        ) : null}
        {auditHasLoaded && auditEntries.length > 0 ? (
          <div className="parameters-table-view parameters-audit__table-wrap">
            <table className="ticket-table parameters-audit-table">
              <caption className="visually-hidden">
                Parametre değişiklik geçmişi
              </caption>
              <thead>
                <tr>
                  <th scope="col">Zaman</th>
                  <th scope="col">Anahtar</th>
                  <th scope="col">Eski</th>
                  <th scope="col">Yeni</th>
                  <th scope="col">Kullanıcı</th>
                </tr>
              </thead>
              <tbody>
                {auditEntries.map((entry) => (
                  <tr key={entry.id}>
                    <td>
                      <time dateTime={entry.changedAt}>
                        {formatTicketActivity(entry.changedAt)}
                      </time>
                    </td>
                    <td className="parameters-table__key">
                      <code>{entry.parameterKey}</code>
                    </td>
                    <td>
                      <code>{entry.oldValue}</code>
                    </td>
                    <td>
                      <code>{entry.newValue}</code>
                    </td>
                    <td>
                      {entry.changedByUsername ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </details>
    </section>
  )
}
