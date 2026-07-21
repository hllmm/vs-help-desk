export function MailWorkflowIllustration() {
  return (
    <div className="mail-workflow" aria-hidden="true">
      <div className="mail-workflow__node">
        <div className="mail-workflow__icon mail-workflow__icon--envelope">
          <span className="mail-workflow__envelope-flap" />
          <span className="mail-workflow__envelope-body" />
        </div>
        <span className="mail-workflow__label">E-posta</span>
      </div>

      <div className="mail-workflow__connector" />

      <div className="mail-workflow__node">
        <div className="mail-workflow__icon mail-workflow__icon--ticket">
          <span className="mail-workflow__ticket-strip">VS-000042</span>
        </div>
        <span className="mail-workflow__label">Destek talebi</span>
      </div>

      <div className="mail-workflow__connector" />

      <div className="mail-workflow__node">
        <div className="mail-workflow__icon mail-workflow__icon--track">
          <span className="mail-workflow__track-dot" />
          <span className="mail-workflow__track-dot" />
          <span className="mail-workflow__track-dot" />
        </div>
        <span className="mail-workflow__label">Takip</span>
      </div>
    </div>
  )
}
