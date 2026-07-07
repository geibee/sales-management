import { Badge } from "@/components/atoms/badge";
import { Button } from "@/components/atoms/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/atoms/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/atoms/dialog";
import { Input } from "@/components/atoms/input";
import { Label } from "@/components/atoms/label";
import { FieldError } from "@/components/molecules";
import type {
  ConsignmentSalesCaseDetail,
  DirectSalesCaseDetail,
  ReservationSalesCaseDetail,
} from "@/contracts";
import { describeApiError } from "@/lib/api-client";
import { formatAmount } from "@/lib/format";
import {
  type AppraisalRateRow,
  RATE_DISPLAY_DEFAULT,
  RATE_DISPLAY_MAX,
  RATE_DISPLAY_MIN,
  apiToDisplayRate,
  computeEstimatedTotal as computeEstimatedTotalRows,
  displayToApiRate,
} from "@/lib/rate";
import {
  CalendarDays,
  CircleDollarSign,
  ClipboardCheck,
  Package,
  ReceiptText,
  Send,
  Truck,
  UserRound,
} from "lucide-react";
import {
  type ChangeEvent,
  type FocusEvent,
  type ReactNode,
  useActionState,
  useRef,
  useState,
} from "react";
import { toast } from "sonner";

type ActionBody = Record<string, unknown>;
type SubmitBody = (body: ActionBody) => Promise<void>;

type FieldErrors = Record<string, string>;

type BaseFormProps = {
  title: string;
  buttonLabel: string;
  version: number;
  disabled?: boolean | undefined;
  disabledReason?: string | undefined;
  onSubmit: SubmitBody;
};

export function DirectAppraisalForm({
  data,
  title,
  buttonLabel,
  disabled,
  disabledReason,
  onSubmit,
}: Omit<BaseFormProps, "version"> & { data: DirectSalesCaseDetail }) {
  const appraisalDate = data.appraisal?.appraisalDate ?? data.salesDate;
  const deliveryDate = data.appraisal?.deliveryDate ?? data.salesDate;
  const salesMarket = data.appraisal?.salesMarket ?? "国内卸売";

  // 税抜査定合計は入力された明細から自動計算する（= Σ 基準単価 × 期中調整率 × 取引先調整率 × 例外調整率）。
  // フォームの各明細フィールド初期値（基準単価1000 / 各調整率1）から初期値を求める。
  const [estimatedTotal, setEstimatedTotal] = useState(() => data.lots.length * 1000);

  const recomputeTotal = (event: ChangeEvent<HTMLFormElement>) => {
    setEstimatedTotal(computeEstimatedTotal(new FormData(event.currentTarget), data.lots.length));
  };

  const { action, isPending, errors, onBlur } = useRichAction(title, disabled, onSubmit, (r) => {
    const lotAppraisals = data.lots.map((lotNumber, index) => ({
      lotNumber,
      detailAppraisals: [
        {
          detailIndex: 1,
          baseUnitPrice: r.requiredInt(`baseUnitPrice-${index}`, "基準単価", { min: 0 }),
          periodAdjustmentRate: r.requiredRate(`periodAdjustmentRate-${index}`, "期中調整率"),
          counterpartyAdjustmentRate: r.requiredRate(
            `counterpartyAdjustmentRate-${index}`,
            "取引先調整率",
          ),
          exceptionalPeriodAdjustmentRate: r.optionalRate(
            `exceptionalPeriodAdjustmentRate-${index}`,
            "例外調整率",
          ),
        },
      ],
    }));

    return {
      type: "normal",
      appraisalDate: r.requiredString("appraisalDate", "査定日"),
      deliveryDate: r.requiredString("deliveryDate", "納期"),
      salesMarket: r.requiredString("salesMarket", "販売市場"),
      baseUnitPriceDate: r.requiredString("baseUnitPriceDate", "基準単価日"),
      periodAdjustmentRateDate: r.requiredString("periodAdjustmentRateDate", "期中調整率日"),
      counterpartyAdjustmentRateDate: r.requiredString(
        "counterpartyAdjustmentRateDate",
        "取引先調整率日",
      ),
      taxExcludedEstimatedTotal: r.requiredInt("taxExcludedEstimatedTotal", "税抜査定合計", {
        min: 0,
      }),
      lotAppraisals,
      version: data.version,
    };
  });

  return (
    <ActionCard
      title={title}
      icon={<CircleDollarSign className="size-4" />}
      version={data.version}
      action={action}
      isPending={isPending}
      onBlur={onBlur}
      onChange={recomputeTotal}
      buttonLabel={buttonLabel}
      disabled={disabled}
      disabledReason={disabledReason}
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <DateField
          name="appraisalDate"
          label="査定日"
          defaultValue={appraisalDate}
          errors={errors}
        />
        <DateField name="deliveryDate" label="納期" defaultValue={deliveryDate} errors={errors} />
        <TextField name="salesMarket" label="販売市場" defaultValue={salesMarket} errors={errors} />
        <EstimatedTotalField
          name="taxExcludedEstimatedTotal"
          label="税抜査定合計"
          computedValue={estimatedTotal}
          errors={errors}
        />
        <DateField
          name="baseUnitPriceDate"
          label="基準単価日"
          defaultValue={appraisalDate}
          errors={errors}
        />
        <DateField
          name="periodAdjustmentRateDate"
          label="期中調整率日"
          defaultValue={appraisalDate}
          errors={errors}
        />
        <DateField
          name="counterpartyAdjustmentRateDate"
          label="取引先調整率日"
          defaultValue={appraisalDate}
          errors={errors}
        />
      </div>
      <div className="space-y-3">
        <div className="flex items-center gap-2">
          <Package className="size-4 text-muted-foreground" />
          <p className="font-medium text-sm">ロット別明細</p>
        </div>
        <div className="grid gap-3">
          {data.lots.map((lotNumber, index) => (
            <div key={lotNumber} className="rounded-md border p-3">
              <div className="mb-3 flex items-center justify-between gap-2">
                <span className="font-mono text-sm">{lotNumber}</span>
                <Badge variant="outline">明細 1</Badge>
              </div>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
                <NumberField
                  name={`baseUnitPrice-${index}`}
                  label="基準単価"
                  defaultValue={1000}
                  min={0}
                  errors={errors}
                />
                <NumberField
                  name={`periodAdjustmentRate-${index}`}
                  label="期中調整率(%)"
                  defaultValue={RATE_DISPLAY_DEFAULT}
                  min={RATE_DISPLAY_MIN}
                  max={RATE_DISPLAY_MAX}
                  step="1"
                  errors={errors}
                />
                <NumberField
                  name={`counterpartyAdjustmentRate-${index}`}
                  label="取引先調整率(%)"
                  defaultValue={RATE_DISPLAY_DEFAULT}
                  min={RATE_DISPLAY_MIN}
                  max={RATE_DISPLAY_MAX}
                  step="1"
                  errors={errors}
                />
                <NumberField
                  name={`exceptionalPeriodAdjustmentRate-${index}`}
                  label="例外調整率(%)"
                  min={RATE_DISPLAY_MIN}
                  max={RATE_DISPLAY_MAX}
                  step="1"
                  required={false}
                  errors={errors}
                />
              </div>
            </div>
          ))}
        </div>
      </div>
    </ActionCard>
  );
}

export function SalesContractForm({
  data,
  title,
  buttonLabel,
  disabled,
  disabledReason,
  onSubmit,
}: Omit<BaseFormProps, "version"> & { data: DirectSalesCaseDetail }) {
  const contractDate = data.contract?.contractDate ?? data.salesDate;
  const person = data.contract?.person ?? "sales-operator";
  const customerNumber = data.contract?.customerNumber ?? "C-001";
  const taxExcludedContractAmount = data.contract?.taxExcludedContractAmount ?? 10000;
  const consumptionTax = data.contract?.consumptionTax ?? 1000;
  const contractRateApi = data.contract?.contractAdjustmentRate;
  const contractAdjustmentRate =
    typeof contractRateApi === "number" ? apiToDisplayRate(contractRateApi) : RATE_DISPLAY_DEFAULT;

  const { action, isPending, errors, onBlur } = useRichAction(title, disabled, onSubmit, (r) => ({
    contractDate: r.requiredString("contractDate", "契約日"),
    person: r.requiredString("person", "担当者"),
    buyer: {
      customerNumber: r.requiredString("customerNumber", "顧客番号"),
      agentName: r.optionalString("agentName"),
    },
    salesType: r.requiredInt("salesType", "販売種別", { min: 1 }),
    item: r.requiredString("item", "品目"),
    deliveryMethod: r.requiredString("deliveryMethod", "納入方法"),
    paymentDeferralCondition: r.optionalString("paymentDeferralCondition"),
    salesMethod: r.requiredInt("salesMethod", "販売方法", { min: 1 }),
    usage: r.optionalString("usage"),
    paymentDeferralAmount: r.optionalInt("paymentDeferralAmount", "支払猶予額"),
    taxExcludedContractAmount: r.requiredInt("taxExcludedContractAmount", "税抜契約額", { min: 0 }),
    contractAdjustmentRate: r.requiredRate("contractAdjustmentRate", "契約調整率"),
    consumptionTax: r.requiredInt("consumptionTax", "消費税", { min: 0 }),
    taxExcludedPaymentAmount: r.requiredInt("taxExcludedPaymentAmount", "税抜支払額", { min: 0 }),
    paymentConsumptionTax: r.requiredInt("paymentConsumptionTax", "支払消費税", { min: 0 }),
    version: data.version,
  }));

  return (
    <ActionCard
      title={title}
      icon={<ReceiptText className="size-4" />}
      version={data.version}
      action={action}
      isPending={isPending}
      onBlur={onBlur}
      buttonLabel={buttonLabel}
      disabled={disabled}
      disabledReason={disabledReason}
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <DateField name="contractDate" label="契約日" defaultValue={contractDate} errors={errors} />
        <TextField name="person" label="担当者" defaultValue={person} errors={errors} />
        <TextField
          name="customerNumber"
          label="顧客番号"
          defaultValue={customerNumber}
          errors={errors}
        />
        <TextField name="agentName" label="代理人" required={false} errors={errors} />
        <NumberField name="salesType" label="販売種別" defaultValue={1} min={1} errors={errors} />
        <TextField name="item" label="品目" defaultValue="steel" errors={errors} />
        <TextField name="deliveryMethod" label="納入方法" defaultValue="truck" errors={errors} />
        <TextField
          name="paymentDeferralCondition"
          label="支払猶予条件"
          defaultValue="monthly"
          required={false}
          errors={errors}
        />
        <NumberField name="salesMethod" label="販売方法" defaultValue={1} min={1} errors={errors} />
        <TextField
          name="usage"
          label="用途"
          defaultValue="resale"
          required={false}
          errors={errors}
        />
        <NumberField
          name="paymentDeferralAmount"
          label="支払猶予額"
          min={0}
          required={false}
          errors={errors}
        />
        <NumberField
          name="taxExcludedContractAmount"
          label="税抜契約額"
          defaultValue={taxExcludedContractAmount}
          min={0}
          errors={errors}
        />
        <NumberField
          name="contractAdjustmentRate"
          label="契約調整率(%)"
          defaultValue={contractAdjustmentRate}
          min={RATE_DISPLAY_MIN}
          max={RATE_DISPLAY_MAX}
          step="1"
          errors={errors}
        />
        <NumberField
          name="consumptionTax"
          label="消費税"
          defaultValue={consumptionTax}
          min={0}
          errors={errors}
        />
        <NumberField
          name="taxExcludedPaymentAmount"
          label="税抜支払額"
          defaultValue={taxExcludedContractAmount}
          min={0}
          errors={errors}
        />
        <NumberField
          name="paymentConsumptionTax"
          label="支払消費税"
          defaultValue={consumptionTax}
          min={0}
          errors={errors}
        />
      </div>
    </ActionCard>
  );
}

export function DateVersionActionForm({
  title,
  buttonLabel,
  dateLabel,
  dateField = "date",
  defaultDate,
  version,
  icon,
  disabled,
  disabledReason,
  onSubmit,
}: BaseFormProps & {
  dateLabel: string;
  dateField?: string;
  defaultDate: string;
  icon?: ReactNode;
}) {
  const { action, isPending, errors, onBlur } = useRichAction(title, disabled, onSubmit, (r) => ({
    [dateField]: r.requiredString(dateField, dateLabel),
    version,
  }));

  return (
    <ActionCard
      title={title}
      icon={icon ?? <CalendarDays className="size-4" />}
      version={version}
      action={action}
      isPending={isPending}
      onBlur={onBlur}
      buttonLabel={buttonLabel}
      disabled={disabled}
      disabledReason={disabledReason}
    >
      <DateField name={dateField} label={dateLabel} defaultValue={defaultDate} errors={errors} />
    </ActionCard>
  );
}

export function ReservationPriceForm({
  data,
  disabled,
  disabledReason,
  onSubmit,
}: {
  data: ReservationSalesCaseDetail;
  disabled?: boolean;
  disabledReason?: string;
  onSubmit: SubmitBody;
}) {
  const appraisalDate = data.reservationPrice?.appraisalDate ?? data.salesDate;
  const reservedLotInfo = data.reservationPrice?.reservedLotInfo ?? data.lots.join(", ");
  const reservedAmount = data.reservationPrice?.reservedAmount ?? 10000;

  const { action, isPending, errors, onBlur } = useRichAction(
    "予約価格 登録",
    disabled,
    onSubmit,
    (r) => ({
      appraisalDate: r.requiredString("appraisalDate", "査定日"),
      reservedLotInfo: r.requiredString("reservedLotInfo", "予約ロット情報"),
      reservedAmount: r.requiredInt("reservedAmount", "予約金額", { min: 0 }),
      version: data.version,
    }),
  );

  return (
    <ActionCard
      title="予約価格 登録"
      icon={<CircleDollarSign className="size-4" />}
      version={data.version}
      action={action}
      isPending={isPending}
      onBlur={onBlur}
      buttonLabel="登録"
      disabled={disabled}
      disabledReason={disabledReason}
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <DateField
          name="appraisalDate"
          label="査定日"
          defaultValue={appraisalDate}
          errors={errors}
        />
        <TextField
          name="reservedLotInfo"
          label="予約ロット情報"
          defaultValue={reservedLotInfo}
          errors={errors}
        />
        <NumberField
          name="reservedAmount"
          label="予約金額"
          defaultValue={reservedAmount}
          min={0}
          errors={errors}
        />
      </div>
    </ActionCard>
  );
}

export function ReservationConfirmationForm({
  data,
  disabled,
  disabledReason,
  onSubmit,
}: {
  data: ReservationSalesCaseDetail;
  disabled?: boolean;
  disabledReason?: string;
  onSubmit: SubmitBody;
}) {
  const determinedDate = data.determination?.determinedDate ?? data.salesDate;
  const determinedAmount = data.determination?.determinedAmount ?? 10000;

  const { action, isPending, errors, onBlur } = useRichAction(
    "予約 確定",
    disabled,
    onSubmit,
    (r) => ({
      determinedDate: r.requiredString("determinedDate", "確定日"),
      determinedAmount: r.requiredInt("determinedAmount", "確定金額", { min: 0 }),
      version: data.version,
    }),
  );

  return (
    <ActionCard
      title="予約 確定"
      icon={<ClipboardCheck className="size-4" />}
      version={data.version}
      action={action}
      isPending={isPending}
      onBlur={onBlur}
      buttonLabel="確定"
      disabled={disabled}
      disabledReason={disabledReason}
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <DateField
          name="determinedDate"
          label="確定日"
          defaultValue={determinedDate}
          errors={errors}
        />
        <NumberField
          name="determinedAmount"
          label="確定金額"
          defaultValue={determinedAmount}
          min={0}
          errors={errors}
        />
      </div>
    </ActionCard>
  );
}

export function ConsignmentDesignationForm({
  data,
  disabled,
  disabledReason,
  onSubmit,
}: {
  data: ConsignmentSalesCaseDetail;
  disabled?: boolean;
  disabledReason?: string;
  onSubmit: SubmitBody;
}) {
  const consignorName = data.consignor?.consignorName ?? "委託先A";
  const consignorCode = data.consignor?.consignorCode ?? "CN-001";
  const designatedDate = data.consignor?.designatedDate ?? data.salesDate;

  const { action, isPending, errors, onBlur } = useRichAction(
    "委託指定",
    disabled,
    onSubmit,
    (r) => ({
      consignorName: r.requiredString("consignorName", "委託先名"),
      consignorCode: r.requiredString("consignorCode", "委託先コード"),
      designatedDate: r.requiredString("designatedDate", "指定日"),
      version: data.version,
    }),
  );

  return (
    <ActionCard
      title="委託指定"
      icon={<UserRound className="size-4" />}
      version={data.version}
      action={action}
      isPending={isPending}
      onBlur={onBlur}
      buttonLabel="登録"
      disabled={disabled}
      disabledReason={disabledReason}
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <TextField
          name="consignorName"
          label="委託先名"
          defaultValue={consignorName}
          errors={errors}
        />
        <TextField
          name="consignorCode"
          label="委託先コード"
          defaultValue={consignorCode}
          errors={errors}
        />
        <DateField
          name="designatedDate"
          label="指定日"
          defaultValue={designatedDate}
          errors={errors}
        />
      </div>
    </ActionCard>
  );
}

export function ConsignmentResultForm({
  data,
  disabled,
  disabledReason,
  onSubmit,
}: {
  data: ConsignmentSalesCaseDetail;
  disabled?: boolean;
  disabledReason?: string;
  onSubmit: SubmitBody;
}) {
  const resultDate = data.result?.resultDate ?? data.salesDate;
  const resultAmount = data.result?.resultAmount ?? 10000;

  const { action, isPending, errors, onBlur } = useRichAction(
    "委託結果 登録",
    disabled,
    onSubmit,
    (r) => ({
      resultDate: r.requiredString("resultDate", "結果日"),
      resultAmount: r.requiredInt("resultAmount", "結果金額", { min: 0 }),
      version: data.version,
    }),
  );

  return (
    <ActionCard
      title="委託結果 登録"
      icon={<Truck className="size-4" />}
      version={data.version}
      action={action}
      isPending={isPending}
      onBlur={onBlur}
      buttonLabel="登録"
      disabled={disabled}
      disabledReason={disabledReason}
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <DateField name="resultDate" label="結果日" defaultValue={resultDate} errors={errors} />
        <NumberField
          name="resultAmount"
          label="結果金額"
          defaultValue={resultAmount}
          min={0}
          errors={errors}
        />
      </div>
    </ActionCard>
  );
}

function ActionCard({
  title,
  icon,
  version,
  action,
  isPending,
  buttonLabel,
  disabled,
  disabledReason,
  onBlur,
  onChange,
  children,
}: {
  title: string;
  icon: ReactNode;
  version: number;
  action: (payload: FormData) => void;
  isPending: boolean;
  buttonLabel: string;
  disabled?: boolean | undefined;
  disabledReason?: string | undefined;
  onBlur?: ((event: FocusEvent<HTMLFormElement>) => void) | undefined;
  onChange?: ((event: ChangeEvent<HTMLFormElement>) => void) | undefined;
  children: ReactNode;
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <CardTitle className="flex items-center gap-2 text-base">
            {icon}
            {title}
          </CardTitle>
          <Badge variant="outline">v{version}</Badge>
        </div>
      </CardHeader>
      <CardContent>
        <form action={action} onBlur={onBlur} onChange={onChange} noValidate className="space-y-4">
          <fieldset disabled={disabled || isPending} className="space-y-4 disabled:opacity-60">
            {children}
          </fieldset>
          {disabled && disabledReason && (
            <p className="text-muted-foreground text-xs">{disabledReason}</p>
          )}
          <Button type="submit" disabled={isPending || disabled}>
            <Send className="size-4" />
            {isPending ? "実行中…" : buttonLabel}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

function useRichAction(
  title: string,
  disabled: boolean | undefined,
  onSubmit: SubmitBody,
  buildBody: (reader: FieldReader) => ActionBody,
) {
  const [errors, setErrors] = useState<FieldErrors>({});
  // ユーザーがフォーカスを当てて離れた（または送信した）フィールドだけを記録し、
  // 未操作のフィールドを最初から赤字にしないようにする。
  const touched = useRef<Set<string>>(new Set());

  const showOnlyTouched = (all: FieldErrors): FieldErrors => {
    const visible: FieldErrors = {};
    for (const [name, message] of Object.entries(all)) {
      if (touched.current.has(name)) visible[name] = message;
    }
    return visible;
  };

  // フォーカスが外れた（focusout）タイミングで、そのフィールドを touched に加えて再検証する。
  // フォーム全体を検証しても、表示するのは touched なフィールドのエラーだけ。
  const handleBlur = (event: FocusEvent<HTMLFormElement>) => {
    if (disabled) return;
    const name = (event.target as unknown as HTMLInputElement).name;
    if (!name) return;
    touched.current.add(name);

    const reader = new FieldReader(new FormData(event.currentTarget));
    buildBody(reader);
    setErrors(showOnlyTouched(reader.errors));
  };

  const [, action, isPending] = useActionState(async (_prev: null, fd: FormData) => {
    if (disabled) return null;

    const reader = new FieldReader(fd);
    const body = buildBody(reader);
    if (reader.hasErrors) {
      // 送信時は全フィールドを touched 扱いにして、違反箇所をすべて赤字表示する。
      for (const name of Object.keys(reader.errors)) touched.current.add(name);
      setErrors(reader.errors);
      return null;
    }
    setErrors({});

    try {
      await onSubmit(body);
      toast.success(`${title} を実行しました`);
    } catch (e) {
      toast.error(describeApiError(e));
    }
    return null;
  }, null);

  return { action, isPending, errors, onBlur: handleBlur };
}

function DateField({
  name,
  label,
  defaultValue,
  errors,
}: {
  name: string;
  label: string;
  defaultValue: string;
  errors: FieldErrors;
}) {
  const error = errors[name];
  return (
    <div className="space-y-1">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="date" defaultValue={defaultValue} aria-invalid={!!error} />
      <FieldError message={error} />
    </div>
  );
}

function TextField({
  name,
  label,
  defaultValue,
  required = true,
  errors,
}: {
  name: string;
  label: string;
  defaultValue?: string;
  required?: boolean;
  errors: FieldErrors;
}) {
  const error = errors[name];
  return (
    <div className="space-y-1">
      <Label htmlFor={name}>
        {label}
        {!required && <span className="ml-1 text-muted-foreground text-xs">(任意)</span>}
      </Label>
      <Input id={name} name={name} type="text" defaultValue={defaultValue} aria-invalid={!!error} />
      <FieldError message={error} />
    </div>
  );
}

function NumberField({
  name,
  label,
  defaultValue,
  min,
  max,
  step = "1",
  required = true,
  errors,
}: {
  name: string;
  label: string;
  defaultValue?: number;
  min?: number;
  max?: number;
  step?: string;
  required?: boolean;
  errors: FieldErrors;
}) {
  const error = errors[name];
  return (
    <div className="space-y-1">
      <Label htmlFor={name}>
        {label}
        {!required && <span className="ml-1 text-muted-foreground text-xs">(任意)</span>}
      </Label>
      <Input
        id={name}
        name={name}
        type="number"
        defaultValue={defaultValue}
        min={min}
        max={max}
        step={step}
        aria-invalid={!!error}
      />
      <FieldError message={error} />
    </div>
  );
}

/**
 * 税抜査定合計フィールド。
 * 既定では明細から自動計算した値をラベル表示し（読み取り専用、hidden で送信）、
 * 「変更する」→モーダルで上長承認を確認したうえでのみ直接入力に切り替えられる。
 * 承認情報はフロント側の入力ガードであり、現状バックエンドには保存しない。
 */
// ユーザー情報テーブルが無いため、承認者は固定値とする（編集不可）。
const FIXED_APPROVER = "営業部長（システム既定）";

function EstimatedTotalField({
  name,
  label,
  computedValue,
  errors,
}: {
  name: string;
  label: string;
  computedValue: number;
  errors: FieldErrors;
}) {
  const error = errors[name];

  // 直接入力モード
  const [manual, setManual] = useState(false);
  const [manualValue, setManualValue] = useState(() => String(computedValue));

  // モーダルの一時状態（確定するまで反映しない）
  const [open, setOpen] = useState(false);
  const [approved, setApproved] = useState(false);

  const openModal = () => {
    setApproved(false);
    setOpen(true);
  };

  const enableManualInput = () => {
    setManualValue(String(computedValue));
    setManual(true);
    setOpen(false);
  };

  const backToAuto = () => {
    setManual(false);
  };

  if (manual) {
    return (
      <div className="space-y-1">
        <Label htmlFor={name}>{label}</Label>
        <Input
          id={name}
          name={name}
          type="number"
          min={0}
          value={manualValue}
          onChange={(event) => setManualValue(event.target.value)}
          aria-invalid={!!error}
        />
        <div className="flex items-start justify-between gap-2">
          <p className="text-muted-foreground text-xs">上長承認済み: {FIXED_APPROVER}</p>
          <Button type="button" variant="ghost" size="sm" onClick={backToAuto}>
            自動計算に戻す
          </Button>
        </div>
        <FieldError message={error} />
      </div>
    );
  }

  return (
    <div className="space-y-1">
      <Label htmlFor={name}>{label}</Label>
      <div className="flex items-center gap-2">
        <span className="font-medium text-sm tabular-nums" aria-live="polite">
          {formatAmount(computedValue)}
        </span>
        <Button type="button" variant="outline" size="sm" onClick={openModal}>
          変更する
        </Button>
      </div>
      {/* 自動計算値を送信するための hidden input */}
      <input type="hidden" name={name} value={computedValue} />
      <p className="text-muted-foreground text-xs">
        明細から自動計算: Σ 基準単価 × 期中調整率 × 取引先調整率 × 例外調整率
      </p>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>税抜査定合計を直接入力</DialogTitle>
            <DialogDescription>
              査定合計を自動計算値から変更するには、上長の承認が必要です。承認を得ていることを確認してください。
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3">
            <label className="flex items-start gap-2 text-sm">
              <input
                type="checkbox"
                checked={approved}
                onChange={(event) => setApproved(event.target.checked)}
                className="mt-0.5 size-4"
              />
              <span>上長の承認を得ています</span>
            </label>
            <div className="space-y-1">
              <Label htmlFor={`${name}-approver`}>承認者</Label>
              {/* 承認者は固定値（読み取り専用）。name を付けず FormData / 検証に混入させない */}
              <Input
                id={`${name}-approver`}
                value={FIXED_APPROVER}
                readOnly
                className="bg-muted/50"
              />
            </div>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              キャンセル
            </Button>
            <Button type="button" onClick={enableManualInput} disabled={!approved}>
              直接入力を有効化
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

/**
 * 明細フィールド（基準単価・各調整率）を画面値のまま行に読み出し、
 * 合計計算は純粋関数 `lib/rate.computeEstimatedTotal` に委譲する。
 */
function computeEstimatedTotal(fd: FormData, lotCount: number): number {
  const rows: AppraisalRateRow[] = [];
  for (let index = 0; index < lotCount; index++) {
    const exceptionalRaw = String(fd.get(`exceptionalPeriodAdjustmentRate-${index}`) ?? "").trim();
    rows.push({
      base: toNumber(fd.get(`baseUnitPrice-${index}`)),
      period: toNumber(fd.get(`periodAdjustmentRate-${index}`)),
      counterparty: toNumber(fd.get(`counterpartyAdjustmentRate-${index}`)),
      exceptional: exceptionalRaw === "" ? null : Number(exceptionalRaw),
    });
  }
  return computeEstimatedTotalRows(rows);
}

function toNumber(value: FormDataEntryValue | null): number {
  const raw = String(value ?? "").trim();
  return raw === "" ? Number.NaN : Number(raw);
}

/**
 * FormData を読みながら、違反したフィールドのエラーを name ごとに蓄積するリーダー。
 * 最初の違反で throw せず全項目を検査するため、複数フィールドのエラーを同時に提示できる。
 */
class FieldReader {
  readonly errors: FieldErrors = {};

  constructor(private readonly fd: FormData) {}

  get hasErrors(): boolean {
    return Object.keys(this.errors).length > 0;
  }

  private raw(name: string): string {
    return String(this.fd.get(name) ?? "").trim();
  }

  private fail(name: string, message: string): void {
    if (!this.errors[name]) this.errors[name] = message;
  }

  optionalString(name: string): string {
    return this.raw(name);
  }

  requiredString(name: string, label: string): string {
    const value = this.raw(name);
    if (!value) this.fail(name, `${label}を入力してください`);
    return value;
  }

  requiredNumber(name: string, label: string, opts: { min?: number; max?: number } = {}): number {
    const value = this.raw(name);
    if (!value) {
      this.fail(name, `${label}を入力してください`);
      return Number.NaN;
    }
    const n = Number(value);
    if (!Number.isFinite(n)) {
      this.fail(name, `${label}は数値で入力してください`);
      return Number.NaN;
    }
    if (opts.min !== undefined && n < opts.min) {
      this.fail(name, `${label}は${opts.min}以上で入力してください`);
      return Number.NaN;
    }
    if (opts.max !== undefined && n > opts.max) {
      this.fail(name, `${label}は${opts.max}以下で入力してください`);
      return Number.NaN;
    }
    return n;
  }

  /**
   * 調整率フィールド。画面では百分率（90〜110）で入力し、API には 1/100 した値（0.9〜1.1）で渡す。
   */
  requiredRate(name: string, label: string): number {
    const display = this.requiredNumber(name, label, {
      min: RATE_DISPLAY_MIN,
      max: RATE_DISPLAY_MAX,
    });
    return Number.isFinite(display) ? displayToApiRate(display) : display;
  }

  optionalRate(name: string, label: string): number | null {
    if (this.raw(name) === "") return null;
    const display = this.requiredNumber(name, label, {
      min: RATE_DISPLAY_MIN,
      max: RATE_DISPLAY_MAX,
    });
    return Number.isFinite(display) ? displayToApiRate(display) : null;
  }

  requiredInt(name: string, label: string, opts: { min?: number } = {}): number {
    const value = this.raw(name);
    if (!value) {
      this.fail(name, `${label}を入力してください`);
      return Number.NaN;
    }
    const n = Number(value);
    if (!Number.isFinite(n)) {
      this.fail(name, `${label}は数値で入力してください`);
      return Number.NaN;
    }
    if (!Number.isInteger(n)) {
      this.fail(name, `${label}は整数で入力してください`);
      return Number.NaN;
    }
    if (opts.min !== undefined && n < opts.min) {
      this.fail(name, `${label}は${opts.min}以上で入力してください`);
      return Number.NaN;
    }
    return n;
  }

  optionalInt(name: string, label: string): number | null {
    const value = this.raw(name);
    if (!value) return null;
    const n = Number(value);
    if (!Number.isInteger(n)) {
      this.fail(name, `${label}は整数で入力してください`);
      return null;
    }
    return n;
  }
}
